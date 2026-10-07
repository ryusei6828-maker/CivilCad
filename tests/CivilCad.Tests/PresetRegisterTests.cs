using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.Settings;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

[Collection("UserPresets")]
public class PresetRegisterTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"civilcad_presets_{Guid.NewGuid():N}.json");
            UserPresetStore.FilePath = path; // 本物の %APPDATA% のファイルには書かない
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally
            {
                foreach (var d in SymbolLibrary.User) SymbolLibrary.RemoveUser(d.Kind);
                UserPresetStore.FilePath = UserPresetStore.DefaultPath;
                PresetSettings.Kind = SymbolLibrary.NorthArrow;
                PresetSettings.SizeMm = 20;
                PresetSettings.AngleDeg = 0;
                if (File.Exists(path)) File.Delete(path);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static List<Entity> Sample(CadDocument doc)
    {
        var l = doc.CurrentLayer;
        return
        [
            new LineEntity(l, new Vec2(100, 100), new Vec2(104, 100)) { Color = RgbColor.Red },
            new CircleEntity(l, new Vec2(102, 101), 1),
            new ArcEntity(l, new Vec2(102, 101), 1.5, 0, Math.PI / 2),
            new TextEntity(l, new Vec2(100, 103), "BM", 0.5) { HAlign = TextHAlign.Left },
            new HatchEntity(l, [[new Vec2(100, 100), new Vec2(101, 100), new Vec2(101, 101)]], [], solid: true),
            new PolylineEntity(l, [new Vec2(100, 100), new Vec2(100, 104), new Vec2(103, 104)], false),
        ];
    }

    [Fact]
    public void RegisterSelection_PlacesSameShapeAtRegisteredSize_AndReplaceUndo() => RunSta(() =>
    {
        var doc = new CadDocument { ScaleDenominator = 200 };
        var source = Sample(doc);
        doc.Entities.AddRange(source);
        var canvas = new CadCanvas { Document = doc };
        canvas.SetSelection(source);
        var tool = new PresetRegisterTool(name => new PresetRegisterInput("水準点", ReplaceSelection: true));
        canvas.SetTool(tool);
        tool.OnPoint(new Vec2(100, 100)); // 基準点

        var def = Assert.Single(SymbolLibrary.User);
        Assert.Equal("水準点", def.Name);
        Assert.Equal(6, def.Shapes.Count);
        Assert.Equal(4000 / 200.0 * 1000 / 1000, def.DefaultSizeMm, 9); // 幅 4m (= 高さ 4m) は 1:200 で用紙 20mm
        Assert.Equal(def.Kind, PresetSettings.Kind);
        Assert.True(File.Exists(UserPresetStore.FilePath));

        // 選択した図形は記号 1 つに置き換わり、展開すると元と同じ位置・大きさ
        var symbol = Assert.Single(doc.Entities.OfType<SymbolEntity>());
        Assert.Single(doc.Entities);
        var parts = symbol.Explode().ToList();
        var line = parts.OfType<PolylineEntity>().First(p => p.Points.Count == 2);
        Assert.Equal(104, line.Points[1].X, 9);
        Assert.Equal(RgbColor.Red, line.Color);     // 登録した色を残す
        Assert.Equal(1, parts.OfType<CircleEntity>().Single().Radius, 9);
        Assert.Equal(1.5, parts.OfType<ArcEntity>().Single().Radius, 9);
        var text = parts.OfType<TextEntity>().Single();
        Assert.Equal(0.5, text.Height, 9);
        Assert.Equal(TextHAlign.Left, text.HAlign);
        Assert.True(parts.OfType<HatchEntity>().Single().Solid);

        // 元に戻すと元の図形に戻る
        doc.History.Undo();
        Assert.Equal(6, doc.Entities.Count);
        Assert.Empty(doc.Entities.OfType<SymbolEntity>());
    });

    [Fact]
    public void SavedAndLoaded_AndRemovedPresetStillDrawsPlacedSymbols() => RunSta(() =>
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var def = SymbolLibrary.FromEntities("テスト", Sample(doc), new Vec2(102, 102), doc.PaperMmToMeters(1), out var skipped);
        Assert.Equal(0, skipped);
        SymbolLibrary.AddUser(def);
        UserPresetStore.Save();

        SymbolLibrary.SetUser([]);
        Assert.Empty(SymbolLibrary.User);
        UserPresetStore.Load();
        var loaded = Assert.Single(SymbolLibrary.User);
        Assert.Equal(def.Kind, loaded.Kind);
        Assert.Equal(def.DefaultSizeMm, loaded.DefaultSizeMm, 9);
        Assert.Equal(def.Shapes.Select(s => s.GetType()), loaded.Shapes.Select(s => s.GetType()));
        Assert.Equal(RgbColor.Red, loaded.Shapes.OfType<SymbolPoly>().First().Color);
        Assert.Equal(TextHAlign.Left, loaded.Shapes.OfType<SymbolText>().Single().HAlign);

        // 置いてから削除しても、置いた記号は同じ形で描ける
        var placed = new SymbolEntity(doc.CurrentLayer, def.Kind, Vec2.Zero, def.DefaultSizeMm, doc.PaperMmToMeters(def.DefaultSizeMm));
        Assert.True(SymbolLibrary.RemoveUser(def.Kind));
        Assert.Empty(SymbolLibrary.User);
        Assert.Equal("テスト", placed.Definition.Name);
        Assert.Equal(6, placed.Explode().Count());
        var canvas = new CadCanvas { Document = doc };
        doc.Entities.Add(placed);
        canvas.RedrawAll();
    });

    [Fact]
    public void ImagesAreSkipped()
    {
        var doc = new CadDocument();
        var img = new ImageEntity(doc.CurrentLayer, "a.jpg", 10, 10, Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1));
        var def = SymbolLibrary.FromEntities("x", [img, new LineEntity(doc.CurrentLayer, Vec2.Zero, new Vec2(2, 0))], Vec2.Zero, 1, out var skipped);
        Assert.Equal(1, skipped);
        Assert.Single(def.Shapes);
    }
}
