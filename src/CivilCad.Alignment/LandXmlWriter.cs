using System.Globalization;
using System.Xml.Linq;
using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

/// <summary>
/// 平面線形を LandXML 1.2 (J-LandXML の並び) で書き出す。
/// Alignment/CoordGeom に直線 (Line)・円曲線 (Curve)・クロソイド (Spiral)、AlignPIs に IP、
/// Feature "Interval" に測点間隔を書く。各要素の始点・終点には主要点の名前 (BP・BC1・KA1-1 など) を付ける。
/// 座標は測量座標の「北 東」の順 (LandXmlReader で読み戻せる)。
/// </summary>
public static class LandXmlWriter
{
    private static readonly XNamespace Ns = "http://www.landxml.org/schema/LandXML-1.2";

    /// <param name="name">路線名。</param>
    /// <param name="h">平面線形 (CAD 座標 x = 東, y = 北)。</param>
    /// <param name="ips">IP (BP・IP・EP)。null なら AlignPIs を書かない。</param>
    /// <param name="pitch">測点間隔 (No.杭の間隔, m)。</param>
    /// <param name="planeZone">平面直角座標系の系番号 (分かれば)。</param>
    public static XDocument Build(string name, HorizontalAlignment h, IReadOnlyList<IntersectionPoint>? ips, double pitch,
        int? planeZone = null, string application = "CivilCad", string? version = null)
    {
        var now = DateTime.Now;
        var root = new XElement(Ns + "LandXML",
            new XAttribute("version", "1.2"),
            new XAttribute("date", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XAttribute("time", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new XAttribute("language", "Japanese"),
            new XAttribute("readOnly", "false"),
            new XElement(Ns + "Units",
                new XElement(Ns + "Metric",
                    new XAttribute("areaUnit", "squareMeter"),
                    new XAttribute("linearUnit", "meter"),
                    new XAttribute("volumeUnit", "cubicMeter"),
                    new XAttribute("temperatureUnit", "celsius"),
                    new XAttribute("pressureUnit", "HPA"),
                    new XAttribute("angularUnit", "decimal degrees"),
                    new XAttribute("directionUnit", "decimal degrees"))));
        var cs = new XElement(Ns + "CoordinateSystem",
            new XAttribute("name", "平面直角座標系"),
            new XAttribute("horizontalDatum", "JGD2011"),
            new XAttribute("verticalDatum", "TP"));
        if (planeZone is int zone) cs.Add(new XAttribute("horizontalCoordinateSystemName", $"{zone}(X,Y)"));
        root.Add(cs,
            new XElement(Ns + "Project", new XAttribute("name", name)),
            new XElement(Ns + "Application",
                new XAttribute("name", application),
                new XAttribute("manufacturer", "CivilCad"),
                new XAttribute("version", version ?? "1.0")),
            new XElement(Ns + "Alignments", new XAttribute("name", name), AlignmentElement(name, h, ips, pitch)));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    public static void Write(string path, string name, HorizontalAlignment h, IReadOnlyList<IntersectionPoint>? ips, double pitch,
        int? planeZone = null, string application = "CivilCad", string? version = null) =>
        Build(name, h, ips, pitch, planeZone, application, version).Save(path);

    private static XElement AlignmentElement(string name, HorizontalAlignment h, IReadOnlyList<IntersectionPoint>? ips, double pitch)
    {
        var al = new XElement(Ns + "Alignment",
            new XAttribute("name", name),
            new XAttribute("length", F(h.Length)),
            new XAttribute("staStart", F(h.StartStation)));
        var geom = new XElement(Ns + "CoordGeom");
        foreach (var e in h.Elements) geom.Add(ElementXml(e, h.MainPoints));
        al.Add(geom);
        if (ips is { Count: > 0 })
            al.Add(new XElement(Ns + "AlignPIs",
                ips.Select(ip => new XElement(Ns + "AlignPI", new XElement(Ns + "PI", new XAttribute("name", ip.Name), P(ip.Point))))));
        if (pitch > 0)
            al.Add(new XElement(Ns + "Feature", new XAttribute("name", "Interval"),
                new XElement(Ns + "Property", new XAttribute("label", "main"), new XAttribute("value", F(pitch)))));
        return al;
    }

    private static XElement ElementXml(HorizontalElement e, IReadOnlyList<MainPoint> mainPoints)
    {
        var s = e.StartPoint;
        var t = e.EndPoint;
        var start = Point("Start", s, PointName(mainPoints, e.StartStation));
        var end = Point("End", t, PointName(mainPoints, e.StartStation + e.Length));
        var common = new object[] { new XAttribute("staStart", F(e.StartStation)), new XAttribute("length", F(e.Length)) };
        switch (e)
        {
            case ArcElement arc:
                return new XElement(Ns + "Curve", common,
                    new XAttribute("rot", arc.Turn > 0 ? "ccw" : "cw"),
                    new XAttribute("radius", F(arc.Radius)),
                    start,
                    new XElement(Ns + "Center", P(arc.Center)),
                    end);
            case ClothoidElement cl:
                // PI = 始点と終点の接線の交点
                var u = Vec2.FromAngle(e.StartDirection);
                var v = Vec2.FromAngle(e.EndDirection);
                var den = u.Cross(v);
                var pi = Math.Abs(den) < 1e-12 ? (s + t) / 2 : s + u * ((t - s).Cross(v) / den);
                return new XElement(Ns + "Spiral", common,
                    new XAttribute("radiusStart", cl.Entering ? "INF" : F(cl.Radius)),
                    new XAttribute("radiusEnd", cl.Entering ? F(cl.Radius) : "INF"),
                    new XAttribute("rot", cl.Turn > 0 ? "ccw" : "cw"),
                    new XAttribute("spiType", "clothoid"),
                    start,
                    new XElement(Ns + "PI", P(pi)),
                    end,
                    new XElement(Ns + "Feature",
                        new XElement(Ns + "Property", new XAttribute("label", "A"), new XAttribute("value", F(cl.A)))));
            default:
                return new XElement(Ns + "Line", common, start, end);
        }
    }

    /// <summary>その追加距離にある主要点の名前 (無ければ null)。</summary>
    private static string? PointName(IReadOnlyList<MainPoint> mainPoints, double station) =>
        mainPoints.Where(m => Math.Abs(m.Station - station) < 1e-6).Select(m => m.Name).FirstOrDefault();

    private static XElement Point(string tag, Vec2 p, string? name)
    {
        var x = new XElement(Ns + tag, P(p));
        if (name != null) x.Add(new XAttribute("name", name));
        return x;
    }

    private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    /// <summary>CAD 座標 → 「北 東」。</summary>
    private static string P(Vec2 p) => $"{F(p.Y)} {F(p.X)}";
}
