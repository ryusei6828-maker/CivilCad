using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class PresetTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally
            {
                PresetSettings.Kind = SymbolLibrary.NorthArrow;
                PresetSettings.SizeMm = 20;
                PresetSettings.AngleDeg = 0;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void SamePaperSize_AtAnyScale_AndContinuousPlacement() => RunSta(() =>
    {
        var paperHeights = new List<double>();
        foreach (var scale in new[] { 100.0, 1000.0 })
        {
            var doc = new CadDocument { ScaleDenominator = scale };
            var canvas = new CadCanvas { Document = doc };
            var tool = new PresetTool();
            canvas.SetTool(tool);
            tool.OnPoint(new Vec2(10, 10));
            tool.OnPoint(new Vec2(50, 10)); // 続けて置ける
            var marks = doc.Entities.OfType<SymbolEntity>().ToList();
            Assert.Equal(2, marks.Count);
            Assert.Same(tool, canvas.Tool);
            // 用紙上 20mm: 1:100 なら 2m、1:1000 なら 20m (用紙上の見た目は同じ)
            Assert.Equal(20 * scale / 1000, marks[0].Size, 9);
            Assert.Equal(20, marks[0].SizeMm);
            paperHeights.Add(marks[0].Bounds.Height / scale * 1000); // 用紙上の高さ (mm)
        }
        Assert.Equal(paperHeights[0], paperHeights[1], 9);
    });

    [Fact]
    public void ChangingScale_KeepsPaperSize_AndUndoRestores()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var mark = new SymbolEntity(doc.CurrentLayer, SymbolLibrary.NorthArrow, Vec2.Zero, 20, doc.PaperMmToMeters(20));
        doc.Entities.Add(mark);
        Assert.Equal(10, mark.Size, 9);
        doc.History.Execute(new SetScaleCommand(1000));
        Assert.Equal(20, mark.Size, 9);
        doc.History.Undo();
        Assert.Equal(10, mark.Size, 9);
    }

    [Fact]
    public void SelectedAsOneEntity_MoveRotate()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        var mark = new SymbolEntity(doc.CurrentLayer, SymbolLibrary.NorthArrow, new Vec2(5, 5), 20, 2);
        Assert.Equal(0, mark.DistanceTo(new Vec2(5.1, 5.2)));   // 記号の中のクリックで選べる
        Assert.True(mark.DistanceTo(new Vec2(20, 20)) > 10);
        mark.Translate(new Vec2(1, 0));
        mark.Rotate(new Vec2(6, 5), Math.PI / 2);
        Assert.Equal(new Vec2(6, 5), mark.Position);
        Assert.Equal(Math.PI / 2, mark.Rotation, 9);
        // 90° 回すと「N」は左 (西) 側に来る
        var n = (TextEntity)mark.Explode().Single(e => e is TextEntity);
        Assert.True(n.Position.X < mark.Position.X - 1);
        var clone = (SymbolEntity)mark.Clone();
        Assert.Equal(mark.Kind, clone.Kind);
        Assert.Equal(mark.SizeMm, clone.SizeMm);
    }

    [Fact]
    public void SavedAsPlainShapes_InDxfAndSfc()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        foreach (var def in SymbolLibrary.All)
            doc.Entities.Add(new SymbolEntity(doc.CurrentLayer, def.Kind, new Vec2(doc.Entities.Count * 30, 0), def.DefaultSizeMm, doc.PaperMmToMeters(def.DefaultSizeMm)));
        foreach (var ext in new[] { ".dxf", ".sfc" })
        {
            var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}{ext}");
            try
            {
                if (ext == ".dxf") DxfIO.Save(doc, path); else SfcWriter.Save(doc, path);
                var loaded = new CadDocument();
                if (ext == ".dxf") DxfIO.Load(loaded, path); else SfcIO.Load(loaded, path);
                Assert.Equal(3, loaded.Entities.OfType<TextEntity>().Count(t => t.Text == "N"));
                Assert.True(loaded.Entities.Count >= 10, $"{ext}: {loaded.Entities.Count}");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
