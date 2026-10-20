using System.IO;
using CivilCad.App.Alignment;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class ProjectFileTests
{
    [Fact]
    public void SaveLoad_KeepsAllEntities_Layers_Coordinates_Route_AndSettings()
    {
        var doc = new CadDocument { ScaleDenominator = 1000, PlaneZone = 2 };
        doc.SetSurveyFrame(0.3, new Vec2(10, 20));
        var red = doc.GetOrCreateLayer("路線-中心線", new RgbColor(255, 0, 0));
        red.IsLocked = true;
        var hidden = doc.GetOrCreateLayer("隠す", RgbColor.White);
        hidden.IsVisible = false;
        var l0 = doc.CurrentLayer;
        doc.Entities.AddRange(
        [
            new LineEntity(red, new Vec2(0, 0), new Vec2(100, 50)) { Color = new RgbColor(1, 2, 3), LineWeight = 0.35, LineType = LineType.DashDot, Tag = "ALIGN:本線" },
            new PolylineEntity(l0, [new(0, 0), new(1, 0), new(1, 1)], isClosed: true) { Filled = true },
            new CircleEntity(l0, new Vec2(5, 5), 2),
            new ArcEntity(l0, new Vec2(5, 5), 3, 0.5, 2.5),
            new PointEntity(l0, new Vec2(30, 40)) { Name = "BM1", Z = 12.345, Tags = "BM, 境界" },
            new TextEntity(l0, new Vec2(1, 2), "工事名", 3.5, 0.2) { HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle, FontName = "ＭＳ 明朝", FitWidth = 20, WidthFactor = 0.8 },
            new HatchEntity(hidden, [[new(0, 0), new(4, 0), new(4, 4)]], [new HatchPattern(0.7, 1.5, new Vec2(0, 0))]) { Opacity = 0.5, PatternName = "ANSI31" },
            new ImageEntity(l0, @"C:\写真\オルソ.jpg", 400, 300, new Vec2(0, 0), new Vec2(40, 0), new Vec2(0, 30)) { Page = 2, CropLeft = 0.1, CropRight = 0.9, InFront = true, TransparencyPercent = 30 },
            new SymbolEntity(l0, SymbolLibrary.NorthArrowSimple, new Vec2(50, 50), 15, 15, 0.4) { AutoNorth = false },
            new LeaderEntity(l0, [new(0, 0), new(5, 5), new(10, 5)], isFlag: true, "旗\n2 行目", 2.5, 1.5, LeaderTip.Dot),
            new DimensionEntity(l0, new Vec2(0, 0), new Vec2(10, 0), new Vec2(5, -3), null, 2.5, 2) { Terminator = DimTerminator.Tick, Unit = DimUnit.Mm, Decimals = 0, TextOverride = "10m" },
        ]);
        doc.CurrentLayer = red; // 現在レイヤはロック解除・表示になる
        doc.CurrentLineWeight = 0.5;
        doc.CurrentLineType = LineType.Dashed;
        var input = AlignmentInput.Sample();
        input.Name = "本線";
        input.Pitch = 20;
        input.SubPitch = 10;
        input.LeftWidths = "3.0, 5.5";

        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.ccp");
        try
        {
            var other = new CadDocument();
            other.Entities.Add(new LineEntity(other.CurrentLayer, Vec2.Zero, new Vec2(1, 1)));
            ProjectFile.Save(path, [new ProjectSheet("平面図", doc, input, HasAlignment: true, new Vec2(7, 8), 2.5, @"C:\図面\平面図.dxf"),
                                    new ProjectSheet("縦断図", other, AlignmentInput.Sample(), HasAlignment: false)], activeIndex: 1);
            Assert.Contains("\"工事名\"", File.ReadAllText(path)); // 日本語はそのまま読める形で書く

            var loaded = ProjectFile.Load(path);
            Assert.Equal(0, loaded.SkippedEntities);
            Assert.Equal(1, loaded.ActiveIndex);
            Assert.Equal(["平面図", "縦断図"], loaded.Sheets.Select(s => s.Name));
            var s = loaded.Sheets[0];
            var d = s.Doc;
            Assert.Equal((new Vec2(7, 8), 2.5, @"C:\図面\平面図.dxf"), (s.ViewCenter!.Value, s.ViewScale!.Value, s.SourcePath));

            // 図面の設定
            Assert.Equal(1000, d.ScaleDenominator);
            Assert.Equal(2, d.PlaneZone);
            Assert.Equal(0.3, d.SurveyRotation, 12);
            Assert.Equal(new Vec2(10, 20), d.SurveyPivot);
            Assert.Equal("路線-中心線", d.CurrentLayer.Name);
            Assert.Equal((0.5, LineType.Dashed), (d.CurrentLineWeight!.Value, d.CurrentLineType));
            Assert.False(d.FindLayer("隠す")!.IsVisible);
            Assert.Equal(doc.Layers.Select(l => l.Name), d.Layers.Select(l => l.Name));
            Assert.False(d.History.CanUndo);

            // 図形は種類・順番・中身まで同じ
            Assert.Equal(doc.Entities.Select(e => e.GetType()), d.Entities.Select(e => e.GetType()));
            var line = (LineEntity)d.Entities[0];
            Assert.Equal((new Vec2(100, 50), new RgbColor(1, 2, 3), 0.35, LineType.DashDot, "ALIGN:本線", "路線-中心線"),
                (line.End, line.Color!.Value, line.LineWeight!.Value, line.LineType, line.Tag, line.Layer.Name));
            Assert.True(((PolylineEntity)d.Entities[1]).Filled);
            var arc = (ArcEntity)d.Entities[3];
            Assert.Equal((3.0, 0.5, 2.5), (arc.Radius, arc.StartAngle, arc.EndAngle));
            var pt = (PointEntity)d.Entities[4];
            Assert.Equal(("BM1", 12.345, "BM, 境界"), (pt.Name, pt.Z!.Value, pt.Tags));
            var text = (TextEntity)d.Entities[5];
            Assert.Equal(("工事名", TextHAlign.Center, TextVAlign.Middle, "ＭＳ 明朝", 20.0, 0.8), (text.Text, text.HAlign, text.VAlign, text.FontName, text.FitWidth!.Value, text.WidthFactor));
            var hatch = (HatchEntity)d.Entities[6];
            Assert.Equal((0.5, "ANSI31", 1.5, "隠す"), (hatch.Opacity, hatch.PatternName, hatch.Patterns[0].Spacing, hatch.Layer.Name));
            var img = (ImageEntity)d.Entities[7];
            Assert.Equal((@"C:\写真\オルソ.jpg", 2, 0.1, 0.9, true, 30.0), (img.Path, img.Page, img.CropLeft, img.CropRight, img.InFront, img.TransparencyPercent));
            var sym = (SymbolEntity)d.Entities[8];
            Assert.Equal((SymbolLibrary.NorthArrowSimple, 0.4, false), (sym.Kind, sym.Rotation, sym.AutoNorth));
            var leader = (LeaderEntity)d.Entities[9];
            Assert.Equal(("旗\n2 行目", true, LeaderTip.Dot, 3), (leader.Text, leader.IsFlag, leader.Tip, leader.Points.Count));
            var dim = (DimensionEntity)d.Entities[10];
            Assert.Equal((DimTerminator.Tick, DimUnit.Mm, 0, "10m", (double?)null), (dim.Terminator, dim.Unit, dim.Decimals, dim.TextOverride, dim.Angle));

            // 路線の入力
            Assert.True(s.HasAlignment);
            Assert.Equal(("本線", 20.0, 10.0, "3.0, 5.5", input.Rows.Count), (s.Alignment.Name, s.Alignment.Pitch, s.Alignment.SubPitch, s.Alignment.LeftWidths, s.Alignment.Rows.Count));
            Assert.Equal(input.Rows[2].A1, s.Alignment.Rows[2].A1);
            Assert.Equal(input.Compute().Length, s.Alignment.Compute().Length, 9);
            Assert.False(loaded.Sheets[1].HasAlignment);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsOtherFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.ccp");
        try
        {
            File.WriteAllText(path, "{\"Format\":\"Other\"}");
            Assert.Throws<InvalidDataException>(() => ProjectFile.Load(path));
            File.WriteAllText(path, "not json");
            Assert.Throws<InvalidDataException>(() => ProjectFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImageFoundRelativeToProject_WhenFolderMoved()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}");
        var moved = dir + "_moved";
        Directory.CreateDirectory(Path.Combine(dir, "画像"));
        try
        {
            var image = Path.Combine(dir, "画像", "a.png");
            File.WriteAllBytes(image, [1, 2, 3]);
            var doc = new CadDocument();
            doc.Entities.Add(new ImageEntity(doc.CurrentLayer, image, 10, 10, Vec2.Zero, new Vec2(1, 0), new Vec2(0, 1)));
            ProjectFile.Save(Path.Combine(dir, "p.ccp"), [new ProjectSheet("s", doc, AlignmentInput.Sample(), false)], 0);
            Directory.Move(dir, moved); // フォルダごと移す
            var loaded = ProjectFile.Load(Path.Combine(moved, "p.ccp"));
            Assert.Equal(Path.Combine(moved, "画像", "a.png"), ((ImageEntity)loaded.Sheets[0].Doc.Entities[0]).Path);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(moved)) Directory.Delete(moved, true);
        }
    }
}
