using CivilCad.Alignment;
using CivilCad.Core.Geometry;

namespace CivilCad.Tests;

public class AlignmentTests
{
    private static double Deg(double d) => d * Math.PI / 180;

    [Fact]
    public void Clothoid_LocalPoint_MatchesNumericIntegration()
    {
        const double a = 100, s = 80;
        // 台形則で ∫cos, ∫sin を数値積分して比較
        const int n = 200000;
        double x = 0, y = 0, h = s / n;
        for (var i = 0; i < n; i++)
        {
            var t0 = Clothoid.TangentAngle(a, i * h);
            var t1 = Clothoid.TangentAngle(a, (i + 1) * h);
            x += (Math.Cos(t0) + Math.Cos(t1)) / 2 * h;
            y += (Math.Sin(t0) + Math.Sin(t1)) / 2 * h;
        }
        var p = Clothoid.LocalPoint(a, s);
        Assert.Equal(x, p.X, 6);
        Assert.Equal(y, p.Y, 6);
    }

    [Fact]
    public void SimpleCurve_MatchesTextbookFormulas()
    {
        // 交角 60°, R = 200 の単曲線
        var ip = new Vec2(300, 0);
        var ep = ip + Vec2.FromAngle(Deg(60)) * 300;
        var al = HorizontalAlignment.FromIps(
        [
            new("BP", Vec2.Zero),
            new("IP1", ip, Radius: 200),
            new("EP", ep),
        ]);

        var c = al.Curves[0];
        Assert.Equal(200 * Math.Tan(Deg(30)), c.T1, 9);     // TL = R tan(IA/2)
        Assert.Equal(200 * Deg(60), c.Lc, 9);               // CL = R·IA
        Assert.Equal(1, c.Turn);

        var bc = al.MainPoints.Single(m => m.Name == "BC1");
        Assert.Equal(300 - c.T1, bc.Station, 9);
        var expectedLength = (300 - c.T1) + c.Lc + (300 - c.T2);
        Assert.Equal(expectedLength, al.Length, 9);
        AssertEndsAt(al, ep);
    }

    [Theory]
    [InlineData(150, 150)]
    [InlineData(120, 180)] // 非対称クロソイド
    public void ClothoidCurve_IsContinuousAndReachesEp(double a1, double a2)
    {
        var ip = new Vec2(400, 0);
        var ep = ip + Vec2.FromAngle(Deg(-50)) * 400; // 右カーブ
        var al = HorizontalAlignment.FromIps(
        [
            new("BP", Vec2.Zero),
            new("IP1", ip, Radius: 300, A1: a1, A2: a2),
            new("EP", ep),
        ]);

        Assert.Equal(-1, al.Curves[0].Turn);
        AssertContinuous(al);
        AssertEndsAt(al, ep);

        // 円曲線区間の曲率半径が R になっていること (3点円で確認)
        var arc = al.Elements.OfType<ArcElement>().Single();
        var p1 = arc.Evaluate(0).Point;
        var p2 = arc.Evaluate(arc.Length / 2).Point;
        var p3 = arc.Evaluate(arc.Length).Point;
        Assert.Equal(300, GeoMath.CircleFrom3Points(p1, p2, p3)!.Value.Radius, 6);

        Assert.Equal(new[] { "BP", "KA1-1", "KE1-1", "KE1-2", "KA1-2", "EP" }, al.MainPoints.Select(m => m.Name));
    }

    [Fact]
    public void MultipleIps_AreContinuous()
    {
        var al = Sample();
        AssertContinuous(al);
        AssertEndsAt(al, Vec2.FromSurvey(450, 600));
    }

    [Fact]
    public void OverlappingCurves_Throw()
    {
        Assert.Throws<AlignmentException>(() => HorizontalAlignment.FromIps(
        [
            new("BP", Vec2.Zero),
            new("IP1", new Vec2(100, 0), Radius: 500),
            new("IP2", new Vec2(100, 100), Radius: 500),
            new("EP", new Vec2(200, 100)),
        ]));
    }

    [Fact]
    public void Project_IsInverseOfPointAt()
    {
        var al = Sample();
        foreach (var st in new[] { 10.0, 123.4, 250.0, al.Length - 5 })
        {
            foreach (var off in new[] { -7.5, 0.0, 3.25 })
            {
                var p = al.PointAt(st, off);
                var (s2, o2) = al.Project(p);
                Assert.Equal(st, s2, 4);
                Assert.Equal(off, o2, 4);
            }
        }
    }

    [Theory]
    [InlineData(0, "No.0")]
    [InlineData(20, "No.1")]
    [InlineData(252.5, "No.12+12.500")]
    [InlineData(59.99999, "No.3")]
    public void Station_Format(double station, string expected) =>
        Assert.Equal(expected, Station.Format(station));

    [Theory]
    [InlineData("No.12+12.5", 252.5)]
    [InlineData("no.3", 60)]
    [InlineData("3+5", 65)]
    [InlineData("72.5", 72.5)]
    public void Station_Parse(string text, double expected)
    {
        Assert.True(Station.TryParse(text, out var st));
        Assert.Equal(expected, st, 9);
    }

    [Fact]
    public void VerticalCurve_Elevations()
    {
        // +2% → -1%, VCL = 100 at 200m, EL 10
        var va = new VerticalAlignment([new(0, 6), new(200, 10, 100), new(400, 8)]);
        Assert.Equal(8.0, va.ElevationAt(100), 9);         // 勾配区間
        Assert.Equal(9.0, va.ElevationAt(150), 9);         // BVC
        Assert.Equal(9.5, va.ElevationAt(250), 9);         // EVC
        // 変化点での縦距 y = (g1-g2)L/8 = 0.03*100/8
        Assert.Equal(10 - 0.375, va.ElevationAt(200), 9);
    }

    internal static HorizontalAlignment Sample() => HorizontalAlignment.FromIps(
    [
        new("BP", Vec2.FromSurvey(0, 0)),
        new("IP1", Vec2.FromSurvey(200, 150), Radius: 300, A1: 150, A2: 150),
        new("IP2", Vec2.FromSurvey(250, 450), Radius: 250, A1: 120, A2: 120),
        new("EP", Vec2.FromSurvey(450, 600)),
    ]);

    private static void AssertContinuous(HorizontalAlignment al)
    {
        for (var i = 0; i + 1 < al.Elements.Count; i++)
        {
            var a = al.Elements[i];
            var b = al.Elements[i + 1];
            Assert.True(a.EndPoint.DistanceTo(b.StartPoint) < 1e-6, $"要素 {i} と {i + 1} の位置が不連続");
            Assert.True(Math.Abs(GeoMath.NormalizeSignedAngle(a.EndDirection - b.StartDirection)) < 1e-9, $"要素 {i} と {i + 1} の方向が不連続");
            Assert.Equal(a.EndStation, b.StartStation, 9);
        }
    }

    private static void AssertEndsAt(HorizontalAlignment al, Vec2 ep) =>
        Assert.True(al.Elements[^1].EndPoint.DistanceTo(ep) < 1e-6);
}
