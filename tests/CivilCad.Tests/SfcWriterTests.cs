using System.IO;
using System.Text;
using CivilCad.App.Drawing;
using CivilCad.App.IO;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class SfcWriterTests
{
    private static readonly Vec2 FrameOrigin = new(1000, 2000);

    private static CadDocument MakeDrawing()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        doc.Entities.AddRange(DrawingFrame.Build(doc, new DrawingFrameOptions { ProjectName = "テスト工事", Client = "𠮷野家" }, FrameOrigin));

        var road = doc.GetOrCreateLayer("D-STR", RgbColor.Red);
        var hidden = doc.GetOrCreateLayer("非表示", RgbColor.Green);
        hidden.IsVisible = false;
        doc.Entities.AddRange(
        [
            new LineEntity(road, new Vec2(1100, 2100), new Vec2(1200, 2150)) { LineWeight = 0.35 },
            new ArcEntity(road, new Vec2(1150, 2100), 20, 0, Math.PI / 2) { Color = new RgbColor(12, 34, 56) },
            new CircleEntity(road, new Vec2(1050, 2050), 5) { LineWeight = 0.3 },
            new PolylineEntity(road, [new(1010, 2010), new(1020, 2010), new(1020, 2020)], true) { Filled = true, Color = RgbColor.Cyan },
            new TextEntity(road, new Vec2(1100, 2200), "No.5 測点", 1.75, Math.PI / 6) { HAlign = TextHAlign.Right, VAlign = TextVAlign.Top },
            new LineEntity(hidden, new Vec2(1000, 2000), new Vec2(1001, 2001)),
        ]);
        return doc;
    }

    private static (CadDocument Loaded, string Text) RoundTrip(CadDocument doc)
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.sfc");
        try
        {
            SfcWriter.Save(doc, path);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(932).GetString(File.ReadAllBytes(path));
            var loaded = new CadDocument();
            SfcIO.Load(loaded, path);
            return (loaded, text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sfc_RoundTrip_KeepsGeometry()
    {
        var doc = MakeDrawing();
        var (loaded, text) = RoundTrip(doc);

        // A1 横、図枠は用紙の (20,20) に来る → 読み直すと (20,20) × 0.5 = (10,10) m
        Assert.Contains(@"drawing_sheet_feature(\'", text);
        Assert.Contains("'1','1','841.000000','594.000000')", text);
        Assert.Equal(500, loaded.ScaleDenominator);
        Assert.Equal(doc.Entities.Count, loaded.Entities.Count);

        // 読み込み側は用紙原点基準になるので、図枠の左下との相対位置で比べる
        var shift = new Vec2(10, 10) - FrameOrigin;
        var frame = loaded.Entities.OfType<PolylineEntity>().Single(p => p.Layer.Name == DrawingFrame.LayerFrame);
        AssertNear(new Vec2(10, 10), frame.Bounds.Min);
        Assert.Equal(1.4, frame.LineWeight);

        var line = loaded.Entities.OfType<LineEntity>().Single(l => l.Layer.Name == "D-STR");
        AssertNear(new Vec2(1100, 2100) + shift, line.Start);
        AssertNear(new Vec2(1200, 2150) + shift, line.End);
        Assert.Equal(0.35, line.LineWeight);
        Assert.Equal(new RgbColor(255, 0, 0), line.Color);

        var arc = loaded.Entities.OfType<ArcEntity>().Single();
        AssertNear(new Vec2(1150, 2100) + shift, arc.Center);
        Assert.Equal(20, arc.Radius, 6);
        Assert.Equal(Math.PI / 2, arc.EndAngle, 6);
        Assert.Equal(new RgbColor(12, 34, 56), arc.Color); // ユーザー定義色

        Assert.Equal(0.3, loaded.Entities.OfType<CircleEntity>().Single().LineWeight!.Value, 6); // ユーザー定義線幅

        var fill = loaded.Entities.OfType<PolylineEntity>().Single(p => p.Filled);
        Assert.Equal(50, fill.Area, 6);
        Assert.Equal(RgbColor.Cyan, fill.Color);

        var t = loaded.Entities.OfType<TextEntity>().Single(x => x.Text == "No.5 測点");
        AssertNear(new Vec2(1100, 2200) + shift, t.Position);
        Assert.Equal(1.75, t.Height, 6);
        Assert.Equal(Math.PI / 6, t.Rotation, 6);
        Assert.Equal((TextHAlign.Right, TextVAlign.Top), (t.HAlign, t.VAlign));

        // Shift_JIS に無い文字 (𠮷) も \X2\ 表記で往復する
        Assert.Contains(loaded.Entities.OfType<TextEntity>(), x => x.Text == "𠮷野家");
        Assert.False(loaded.FindLayer("非表示")!.IsVisible);
    }

    [Fact]
    public void Sfc_WithoutFrame_PicksFittingPaper()
    {
        var doc = new CadDocument { ScaleDenominator = 100 };
        // 30m × 20m → 1/100 で 300×200mm + 余白 → A3 (420×297)
        doc.Entities.Add(new PolylineEntity(doc.CurrentLayer, [new(0, 0), new(30, 0), new(30, 20)]));
        var (paper, _, _, _) = SfcWriter.Layout(doc);
        Assert.Equal("A3", paper!.Name);

        var (loaded, _) = RoundTrip(doc);
        var pl = loaded.Entities.OfType<PolylineEntity>().Single();
        Assert.Equal(30, pl.Bounds.Width, 6);
        Assert.Equal(20, pl.Bounds.Height, 6);
        Assert.Equal(100, loaded.ScaleDenominator);
    }

    private static void AssertNear(Vec2 expected, Vec2 actual) =>
        Assert.True(expected.DistanceTo(actual) < 1e-4, $"expected {expected}, actual {actual}");
}
