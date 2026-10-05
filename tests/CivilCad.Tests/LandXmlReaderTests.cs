using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CivilCad.Alignment;
using CivilCad.Core.Geometry;

namespace CivilCad.Tests;

public class LandXmlReaderTests
{
    /// <summary>IP 法で作った線形を LandXML の CoordGeom に書き出し、読み戻して IP 表が元に戻るか。</summary>
    [Fact]
    public void RoundTrip_LineSpiralCurve()
    {
        var ips = new List<IntersectionPoint>
        {
            new("BP", Vec2.FromSurvey(-95437.317, -60763.757)),
            new("IP.1", Vec2.FromSurvey(-95559.866, -60779.875), 300, 110, 110), // 右カーブ・クロソイドあり
            new("IP.2", Vec2.FromSurvey(-95706.041, -60856.788), 160, 0, 0),     // 円曲線のみ
            new("IP.3", Vec2.FromSurvey(-95950.000, -60950.000), 50, 35, 0),    // 左カーブ・入口だけクロソイド
            new("EP", Vec2.FromSurvey(-96150.000, -60900.000)),
        };
        var original = HorizontalAlignment.FromIps(ips, 460);
        var xml = ToLandXml(original, withPis: true, ("IP.2", ips[2].Point));

        var read = Assert.Single(LandXmlReader.Parse(xml));
        Assert.Equal("中心線形", read.Name);
        Assert.Equal(460, read.StartStation);
        Assert.Equal(20, read.Pitch);
        Assert.Equal(ips.Count, read.Ips.Count);
        for (var i = 0; i < ips.Count; i++)
        {
            Assert.True(ips[i].Point.DistanceTo(read.Ips[i].Point) < 1e-4, $"{ips[i].Name}: {read.Ips[i].Point}");
            Assert.Equal(ips[i].Radius, read.Ips[i].Radius, 6);
            Assert.Equal(ips[i].A1, read.Ips[i].A1, 6);
            Assert.Equal(ips[i].A2, read.Ips[i].A2, 6);
        }
        Assert.Equal("IP.2", read.Ips[2].Name);

        var again = HorizontalAlignment.FromIps(read.Ips, read.StartStation);
        Assert.Equal(original.Length, again.Length, 4);
        Assert.Equal(read.FileLength, again.Length, 4);
    }

    [Fact]
    public void RoundTrip_WithoutSpiralPi()
    {
        var ips = new List<IntersectionPoint>
        {
            new("BP", new Vec2(0, 0)),
            new("IP1", new Vec2(200, 0), 150, 70, 60),
            new("IP2", new Vec2(330, 160), 120, 50, 50),
            new("EP", new Vec2(520, 170)),
        };
        var original = HorizontalAlignment.FromIps(ips);
        var read = Assert.Single(LandXmlReader.Parse(ToLandXml(original, withPis: false)));
        for (var i = 0; i < ips.Count; i++)
            Assert.True(ips[i].Point.DistanceTo(read.Ips[i].Point) < 1e-4, $"{ips[i].Name}: {read.Ips[i].Point}");
        Assert.Equal(70, read.Ips[1].A1, 6);
        Assert.Equal(60, read.Ips[1].A2, 6);
        Assert.Equal(original.Length, HorizontalAlignment.FromIps(read.Ips).Length, 4);
    }

    [Fact]
    public void LinesOnly_BendPointsBecomeIpsWithoutCurve()
    {
        var xml = XDocument.Parse("""
            <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2"><Alignments><Alignment name="函渠" staStart="0" length="25"><CoordGeom>
            <Line><Start name="BP">0 0</Start><End name="KIP1">10 0</End></Line>
            <Line><Start name="KIP1">10 0</Start><End name="KEP">10 15</End></Line>
            </CoordGeom></Alignment></Alignments></LandXML>
            """);
        var read = Assert.Single(LandXmlReader.Parse(xml));
        Assert.Equal(["BP", "KIP1", "KEP"], read.Ips.Select(i => i.Name));
        Assert.Equal(0, read.Ips[1].Radius);

        // 折れ点で曲がる (IP を通る) こと
        var h = HorizontalAlignment.FromIps(read.Ips);
        Assert.Equal(25, h.Length, 9);
        Assert.True(h.Evaluate(10).Point.DistanceTo(Vec2.FromSurvey(10, 0)) < 1e-9);
        Assert.True(h.Evaluate(20).Point.DistanceTo(Vec2.FromSurvey(10, 10)) < 1e-9);
    }

    [Fact]
    public void EggShapedClothoid_IsRejected()
    {
        var xml = XDocument.Parse("""
            <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2"><Alignments><Alignment name="x" staStart="0"><CoordGeom>
            <Line><Start>0 0</Start><End>0 10</End></Line>
            <Spiral length="10" radiusStart="300" radiusEnd="200" rot="cw" spiType="clothoid"><Start>0 10</Start><End>0.1 20</End></Spiral>
            </CoordGeom></Alignment></Alignments></LandXML>
            """);
        Assert.Throws<AlignmentException>(() => LandXmlReader.Parse(xml));
    }

    // ---- LandXML を書き出す (テスト用) ----

    private static string P(Vec2 p)
    {
        var (x, y) = p.ToSurvey();
        return string.Create(CultureInfo.InvariantCulture, $"{x:F6} {y:F6}");
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static XDocument ToLandXml(HorizontalAlignment al, bool withPis, params (string Name, Vec2 Point)[] alignPis)
    {
        var sb = new StringBuilder();
        sb.Append($"""<LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2"><Alignments><Alignment name="中心線形" length="{F(al.Length)}" staStart="{F(al.StartStation)}"><CoordGeom>""");
        foreach (var e in al.Elements)
        {
            var s = e.StartPoint;
            var t = e.EndPoint;
            switch (e)
            {
                case LineElement:
                    sb.Append($"<Line length=\"{F(e.Length)}\"><Start>{P(s)}</Start><End>{P(t)}</End></Line>");
                    break;
                case ArcElement arc:
                    sb.Append($"<Curve rot=\"{(arc.Turn > 0 ? "ccw" : "cw")}\" radius=\"{F(arc.Radius)}\" length=\"{F(e.Length)}\"><Start>{P(s)}</Start><Center>{P(arc.Center)}</Center><End>{P(t)}</End></Curve>");
                    break;
                case ClothoidElement cl:
                    var r0 = cl.Entering ? "INF" : F(cl.Radius);
                    var r1 = cl.Entering ? F(cl.Radius) : "INF";
                    var pi = "";
                    if (withPis)
                    {
                        var u = Vec2.FromAngle(e.StartDirection);
                        var v = Vec2.FromAngle(e.EndDirection);
                        var ip = s + u * ((t - s).Cross(v) / u.Cross(v));
                        pi = $"<PI>{P(ip)}</PI>";
                    }
                    sb.Append($"<Spiral length=\"{F(e.Length)}\" radiusStart=\"{r0}\" radiusEnd=\"{r1}\" rot=\"{(cl.Turn > 0 ? "ccw" : "cw")}\" spiType=\"clothoid\"><Start>{P(s)}</Start>{pi}<End>{P(t)}</End><Feature><Property label=\"A\" value=\"{F(cl.A)}\"/></Feature></Spiral>");
                    break;
            }
        }
        sb.Append("</CoordGeom><AlignPIs>");
        foreach (var (name, p) in alignPis) sb.Append($"<AlignPI><PI name=\"{name}\">{P(p)}</PI></AlignPI>");
        sb.Append("</AlignPIs>");
        sb.Append("<Feature name=\"Interval\"><Property label=\"main\" value=\"20.000\"/></Feature></Alignment></Alignments></LandXML>");
        var doc = XDocument.Parse(sb.ToString());
        return doc;
    }
}
