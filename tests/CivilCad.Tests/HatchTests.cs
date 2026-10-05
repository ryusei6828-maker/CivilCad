using System.IO;
using System.Runtime.ExceptionServices;
using CivilCad.App.IO;
using CivilCad.App.Tools;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

public class HatchTests
{
    private static readonly Layer L = new("0", RgbColor.White);

    private static List<Vec2> Rect(double x0, double y0, double x1, double y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Lines_ClippedToBoundary_WithHole()
    {
        // 10×10 の正方形、水平線を 1m 間隔 (y = 0.5, 1.5, …)
        var h = new HatchEntity(L, [Rect(0, 0, 10, 10)], [new HatchPattern(0, 1, new Vec2(0, 0.5))]);
        Assert.Equal(10, h.Lines.Count);
        Assert.All(h.Lines, s => Assert.Equal(10, s.A.DistanceTo(s.B), 9));
        Assert.Equal(100, h.Area, 9);

        // 中央に 4×4 の中抜き: y = 3.5〜6.5 の 4 本は 2 本ずつに分かれる
        var holed = new HatchEntity(L, [Rect(0, 0, 10, 10), Rect(3, 3, 7, 7)], [new HatchPattern(0, 1, new Vec2(0, 0.5))]);
        Assert.Equal(14, holed.Lines.Count);
        Assert.Equal(84, holed.Area, 9);
        Assert.False(holed.Contains(new Vec2(5, 5)));
        Assert.True(holed.Contains(new Vec2(1, 1)));
        Assert.Equal(100 - 16 * 4 / 4.0 * 1, holed.Lines.Sum(s => s.A.DistanceTo(s.B)), 9); // 10 本 × 10m − 4 本 × 4m
    }

    [Fact]
    public void Translate_Rotate_MoveLinesTogether()
    {
        var h = new HatchEntity(L, [Rect(0, 0, 10, 10)], [new HatchPattern(Math.PI / 4, 1, Vec2.Zero)]);
        var before = h.Lines.Count;
        h.Translate(new Vec2(100, 50));
        Assert.Equal(before, h.Lines.Count);
        Assert.All(h.Lines, s => Assert.True(h.Bounds.Inflate(1e-9).Contains(s.A) && h.Bounds.Inflate(1e-9).Contains(s.B)));
        var c = (HatchEntity)h.Clone();
        c.Rotate(new Vec2(105, 55), Math.PI / 2);
        Assert.Equal(3 * Math.PI / 4, c.Patterns[0].Angle, 9);
        Assert.Equal(before, c.Lines.Count); // 正方形を中心で 90° 回しても本数は同じ
    }

    [Fact]
    public void FindRegion_SmallestOuter_AndHoles()
    {
        var doc = new CadDocument();
        var layer = doc.CurrentLayer;
        doc.Entities.Add(new PolylineEntity(layer, Rect(0, 0, 20, 20), true));
        doc.Entities.Add(new CircleEntity(layer, new Vec2(10, 10), 3));
        doc.Entities.Add(new PolylineEntity(layer, Rect(100, 0, 110, 10), false)); // 開いた図形は対象外

        var region = HatchTool.FindRegion(doc, new Vec2(2, 2))!;
        Assert.Equal(2, region.Count);                 // 外形 + 円の中抜き
        Assert.Equal(400, Math.Abs(HatchEntity.SignedArea(region[0])), 6);

        var inCircle = HatchTool.FindRegion(doc, new Vec2(10, 10))!;
        Assert.Single(inCircle);                       // 円の中をクリックすると円だけ
        Assert.Null(HatchTool.FindRegion(doc, new Vec2(105, 5)));
    }

    [Fact]
    public void Tool_ClickInside_ThenSelectAsOneEntity() => RunSta(() =>
    {
        var doc = new CadDocument();
        doc.Entities.Add(new PolylineEntity(doc.CurrentLayer, Rect(0, 0, 20, 20), true));
        var canvas = new CadCanvas { Document = doc };
        canvas.SetView(new Vec2(10, 10), 10);
        HatchSettings.Style = HatchStyle.Cross;
        HatchSettings.AngleDeg = 30;
        HatchSettings.SpacingMm = 4; // 1:500 で 2m
        try
        {
            var tool = new HatchTool();
            canvas.SetTool(tool);
            tool.OnPoint(new Vec2(5, 5));
            var hatch = Assert.Single(doc.Entities.OfType<HatchEntity>());
            Assert.Equal(2, hatch.Patterns.Count);
            Assert.Equal(2, hatch.Patterns[0].Spacing, 9);
            Assert.Equal(30 * Math.PI / 180, hatch.Patterns[0].Angle, 9);

            // ハッチ線の上をクリックすると、線 1 本ではなくハッチング全体が選ばれる
            var (a, b) = hatch.Lines[hatch.Lines.Count / 2];
            Assert.Same(hatch, canvas.HitTest((a + b) / 2));

            // 点指定: 3 点 + Enter
            tool.OnPoint(new Vec2(30, 0));
            tool.OnPoint(new Vec2(40, 0));
            tool.OnPoint(new Vec2(40, 10));
            tool.OnEnter();
            Assert.Equal(2, doc.Entities.OfType<HatchEntity>().Count());
        }
        finally
        {
            HatchSettings.Style = HatchStyle.Single;
            HatchSettings.AngleDeg = 45;
            HatchSettings.SpacingMm = 3;
        }
    });

    private static CadDocument Sample()
    {
        var doc = new CadDocument { ScaleDenominator = 500 };
        var layer = doc.GetOrCreateLayer("D-HTC", RgbColor.Red);
        doc.Entities.Add(new HatchEntity(layer, [Rect(1000, 2000, 1020, 2010), Rect(1005, 2003, 1008, 2006)],
            [new HatchPattern(Math.PI / 4, 1.5, new Vec2(1000, 2000)), new HatchPattern(3 * Math.PI / 4, 1.5, new Vec2(1000, 2000))]) { LineWeight = 0.18 });
        doc.Entities.Add(new HatchEntity(layer, [Rect(1030, 2000, 1040, 2010)], [], solid: true) { Color = RgbColor.Cyan });
        return doc;
    }

    private static void AssertSame(CadDocument loaded)
    {
        var lined = Assert.Single(loaded.Entities.OfType<HatchEntity>(), h => !h.Solid);
        Assert.Equal(2, lined.Loops.Count);
        Assert.Equal(2, lined.Patterns.Count);
        Assert.Equal(1.5, lined.Patterns[0].Spacing, 4);
        Assert.Equal(Math.PI / 4, GeoMath.NormalizeAngle(lined.Patterns[0].Angle), 6);
        Assert.Equal(200 - 9, lined.Area, 3);
        var original = Sample().Entities.OfType<HatchEntity>().First();
        Assert.Equal(original.Lines.Count, lined.Lines.Count);
        // 中抜きの無い塗りは、SFC では塗りつぶしポリラインとして戻る (DXF では塗りのハッチング)
        var solidArea = loaded.Entities.OfType<HatchEntity>().Where(h => h.Solid).Select(h => h.Area)
            .Concat(loaded.Entities.OfType<PolylineEntity>().Where(p => p.Filled).Select(p => p.Area)).Single();
        Assert.Equal(100, solidArea, 3);
    }

    [Fact]
    public void Sfc_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.sfc");
        try
        {
            SfcWriter.Save(Sample(), path);
            var loaded = new CadDocument();
            SfcIO.Load(loaded, path);
            AssertSame(loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dxf_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civilcad_{Guid.NewGuid():N}.dxf");
        try
        {
            DxfIO.Save(Sample(), path);
            var loaded = new CadDocument();
            Assert.Equal(0, DxfIO.Load(loaded, path));
            AssertSame(loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
