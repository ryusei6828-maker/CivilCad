using CivilCad.App.Drawing;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Tests;

/// <summary>図枠を描いてある図形に合わせる (縮尺の選び方と、図形が中央に来る置き方)。</summary>
public class FrameFitTests
{
    private static DrawingFrameOptions A3(double scale) => new() { Paper = "A3", ScaleDenominator = scale, Margin = 20 };

    [Fact]
    public void FitScale_PicksLargestDrawingThatFits()
    {
        // A3 余白 20mm → 外枠 380×257mm。表題欄 (100×60) の上: 360×177mm、左: 260×237mm (余裕 10mm を除く)
        var ext = new Box2(new Vec2(0, 0), new Vec2(8, 4)); // 8m × 4m
        // 上の場所なら 8000/360 = 22.2、4000/177 = 22.6 → 1:22.6 以上 → 1:25
        Assert.Equal(25, DrawingFrame.FitScale(A3(100), ext));
        Assert.True(DrawingFrame.Fits(A3(25), ext));
        Assert.False(DrawingFrame.Fits(A3(20), ext));
        // 大きな図形 (300m × 150m) → 1:1000
        Assert.Equal(1000, DrawingFrame.FitScale(A3(100), new Box2(Vec2.Zero, new Vec2(300, 150))));
    }

    [Fact]
    public void CenteredOrigin_PutsShapesInsideFrameAvoidingTitleBlock()
    {
        var o = A3(25);
        var ext = new Box2(new Vec2(100, 200), new Vec2(108, 204));
        var origin = DrawingFrame.CenteredOrigin(o, ext);
        var doc = new CadDocument();
        var frame = DrawingFrame.Build(doc, o, origin);
        var outer = frame.OfType<PolylineEntity>().First().Bounds;
        Assert.True(outer.Contains(ext));
        // 表題欄 (外枠の右下 100×60mm = 2.5m × 1.5m) と重ならない
        var k = o.MetersPerPaperMm;
        var title = new Box2(new Vec2(outer.Max.X - 100 * k, outer.Min.Y), new Vec2(outer.Max.X, outer.Min.Y + 60 * k));
        Assert.False(title.Intersects(ext));
        // 図形の中心は、表題欄の上の場所の中心
        var free = new Box2(new Vec2(outer.Min.X, outer.Min.Y + 60 * k), outer.Max);
        Assert.True(free.Center.DistanceTo(ext.Center) < 1e-9);
    }

    [Fact]
    public void DrawingExtents_IgnoresFrameAndHiddenLayers()
    {
        var doc = new CadDocument();
        Assert.Null(DrawingFrame.DrawingExtents(doc));
        doc.Entities.Add(new LineEntity(doc.CurrentLayer, new Vec2(0, 0), new Vec2(5, 2)));
        var hidden = doc.GetOrCreateLayer("隠し", RgbColor.White);
        hidden.IsVisible = false;
        doc.Entities.Add(new LineEntity(hidden, new Vec2(0, 0), new Vec2(500, 500)));
        doc.Entities.AddRange(DrawingFrame.Build(doc, A3(100), new Vec2(-100, -100)));
        var ext = DrawingFrame.DrawingExtents(doc)!.Value;
        Assert.Equal((5.0, 2.0), (ext.Width, ext.Height));
    }
}
