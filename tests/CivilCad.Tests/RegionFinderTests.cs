using CivilCad.Core.Geometry;

namespace CivilCad.Tests;

public class RegionFinderTests
{
    private static BoundaryCurve S(double x1, double y1, double x2, double y2) => BoundaryCurve.Segment(new Vec2(x1, y1), new Vec2(x2, y2));

    private static List<BoundaryCurve> Square(double x0, double y0, double x1, double y1) =>
        [S(x0, y0, x1, y0), S(x1, y0, x1, y1), S(x1, y1, x0, y1), S(x0, y1, x0, y0)];

    [Fact]
    public void CrossingLines_FormRectangle()
    {
        // 10 x 5 の長方形 (各線が両端にはみ出す)
        var r = RegionFinder.Find([S(-2, 0, 12, 0), S(-2, 5, 12, 5), S(0, -3, 0, 8), S(10, -3, 10, 8)], new Vec2(5, 2));
        Assert.NotNull(r);
        Assert.Equal(50, r.Area, 9);
        Assert.Equal(30, r.Perimeter, 9);
        Assert.Single(r.Loops);
    }

    [Fact]
    public void SquareSplitByDiagonal_EachSideIsHalf()
    {
        List<BoundaryCurve> curves = [.. Square(0, 0, 10, 10), S(0, 0, 10, 10)];
        var lower = RegionFinder.Find(curves, new Vec2(8, 2));
        var upper = RegionFinder.Find(curves, new Vec2(2, 8));
        Assert.Equal(50, lower!.Area, 9);
        Assert.Equal(50, upper!.Area, 9);
        Assert.Equal(20 + 10 * Math.Sqrt(2), lower.Perimeter, 9);
    }

    [Fact]
    public void Circle_ExactAreaAndPerimeter()
    {
        var r = RegionFinder.Find([BoundaryCurve.Circle(new Vec2(3, 4), 10)], new Vec2(3, 4));
        Assert.NotNull(r);
        Assert.Equal(Math.PI * 100, r.Area, 6);
        Assert.True(Math.Abs(r.Area - Math.PI * 100) / (Math.PI * 100) < 1e-9);
        Assert.Equal(2 * Math.PI * 10, r.Perimeter, 9);
        Assert.Null(RegionFinder.Find([BoundaryCurve.Circle(new Vec2(3, 4), 10)], new Vec2(30, 4)));
    }

    [Fact]
    public void HalfDisc_ArcAndDiameter()
    {
        // 上半分の円弧 (0°→180°) と直径
        var curves = new List<BoundaryCurve> { BoundaryCurve.Arc(Vec2.Zero, 10, 0, Math.PI), S(-10, 0, 10, 0) };
        var r = RegionFinder.Find(curves, new Vec2(0, 3));
        Assert.NotNull(r);
        Assert.True(Math.Abs(r.Area - Math.PI * 50) / (Math.PI * 50) < 1e-9);
        Assert.Equal(Math.PI * 10 + 20, r.Perimeter, 9);
        Assert.Null(RegionFinder.Find(curves, new Vec2(0, -3)));
    }

    [Fact]
    public void LineCrossingCircle_SplitsDisc()
    {
        // 円を x=0 の線が貫く: 左右とも半円
        var curves = new List<BoundaryCurve> { BoundaryCurve.Circle(Vec2.Zero, 10), S(0, -15, 0, 15) };
        var r = RegionFinder.Find(curves, new Vec2(4, 1));
        Assert.True(Math.Abs(r!.Area - Math.PI * 50) / (Math.PI * 50) < 1e-9);
        // 円の外側の、線に挟まれていない所は囲まれていない
        Assert.Null(RegionFinder.Find(curves, new Vec2(12, 0)));
    }

    [Fact]
    public void Island_BecomesHole()
    {
        List<BoundaryCurve> curves = [.. Square(0, 0, 10, 10), .. Square(3, 3, 5, 5)];
        var between = RegionFinder.Find(curves, new Vec2(8, 8));
        Assert.Equal(100 - 4, between!.Area, 9);
        Assert.Equal(40 + 8, between.Perimeter, 9);
        Assert.Equal(2, between.Loops.Count);
        var inner = RegionFinder.Find(curves, new Vec2(4, 4));
        Assert.Equal(4, inner!.Area, 9);
        Assert.Single(inner.Loops);
    }

    [Fact]
    public void Outside_And_OpenShape_AreNull()
    {
        Assert.Null(RegionFinder.Find(Square(0, 0, 10, 10), new Vec2(20, 20)));
        // コの字 (3 本)
        Assert.Null(RegionFinder.Find([S(0, 0, 10, 0), S(10, 0, 10, 10), S(10, 10, 0, 10)], new Vec2(5, 5)));
        Assert.Null(RegionFinder.Find([], new Vec2(0, 0)));
    }

    [Fact]
    public void TJunction_AndDanglingEnds()
    {
        // 上辺が左右の辺の途中に T 字でつく + 余計にはみ出す線
        var curves = new List<BoundaryCurve> { S(0, 0, 10, 0), S(0, 0, 0, 6), S(10, 0, 10, 6), S(-3, 6, 13, 6), S(5, 0, 5, -4) };
        var r = RegionFinder.Find(curves, new Vec2(5, 3));
        Assert.Equal(60, r!.Area, 9);
        Assert.Equal(32, r.Perimeter, 9);
        // 辺の途中から別の線が T 字で立ち上がり、長方形を二つに分ける
        curves.Add(S(4, 0, 4, 6));
        Assert.Equal(24, RegionFinder.Find(curves, new Vec2(2, 3))!.Area, 9);
        Assert.Equal(36, RegionFinder.Find(curves, new Vec2(7, 3))!.Area, 9);
    }

    [Fact]
    public void LensBetweenTwoCircles()
    {
        // 半径 10 の円 2 つ、中心間 10 → レンズ形 = 2 r² acos(d/2r) - (d/2) sqrt(4r² - d²)
        var curves = new List<BoundaryCurve> { BoundaryCurve.Circle(Vec2.Zero, 10), BoundaryCurve.Circle(new Vec2(10, 0), 10) };
        var r = RegionFinder.Find(curves, new Vec2(5, 0));
        var expected = 2 * 100 * Math.Acos(0.5) - 5 * Math.Sqrt(400 - 100);
        Assert.True(Math.Abs(r!.Area - expected) / expected < 1e-9);
    }

    [Fact]
    public void RegionMap_BuildOnce_QueryMany()
    {
        var map = RegionFinder.Build([S(-2, 0, 12, 0), S(-2, 5, 12, 5), S(0, -3, 0, 8), S(10, -3, 10, 8), S(5, -3, 5, 8)]);
        Assert.Equal(2, map.FaceCount);
        Assert.Equal(25, map.Find(new Vec2(2, 2))!.Area, 9);
        Assert.Equal(25, map.Find(new Vec2(8, 3))!.Area, 9);
        Assert.Null(map.Find(new Vec2(-1, 2)));
    }

    [Fact]
    public void RegionMap_ManySegments_BuildsFast()
    {
        var rnd = new Random(1);
        var curves = new List<BoundaryCurve>();
        for (var i = 0; i <= 100; i++)
        {
            curves.Add(S(0, i * 10, 1000, i * 10));
            curves.Add(S(i * 10, 0, i * 10, 1000));
        }
        for (var i = 0; i < 3000; i++)
        {
            var x = rnd.NextDouble() * 1000;
            var y = rnd.NextDouble() * 1000;
            curves.Add(S(x, y, x + rnd.NextDouble() * 6 - 3, y + rnd.NextDouble() * 6 - 3));
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var map = RegionFinder.Build(curves);
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 2, $"build {sw.Elapsed.TotalSeconds:F2}s");
        Assert.True(map.FaceCount > 1000);
        Assert.NotNull(map.Find(new Vec2(55.2, 35.1)));
    }
}
