using System.Globalization;
using System.Xml.Linq;
using CivilCad.Core.Geometry;

namespace CivilCad.Alignment;

/// <summary>LandXML (J-LandXML) から読み込んだ 1 路線。IP は CAD 座標。</summary>
public sealed record LandXmlAlignment(
    string Name,
    double StartStation,
    double? Pitch,
    IReadOnlyList<IntersectionPoint> Ips,
    double FileLength);

/// <summary>
/// LandXML の平面線形 (Alignment/CoordGeom の Line・Curve・Spiral) を読み、IP 法の IP 表に直す。
/// IP は各曲線の手前と先の接線の交点、R は円曲線の半径、A はクロソイドのパラメータ。
/// 座標は LandXML の「北 東」の順 (測量座標 X Y)。
/// </summary>
public static class LandXmlReader
{
    private const double Inf = double.PositiveInfinity;

    public static List<LandXmlAlignment> Read(string path) => Parse(XDocument.Load(path));

    public static List<LandXmlAlignment> Parse(XDocument xml)
    {
        var list = new List<LandXmlAlignment>();
        foreach (var al in xml.Descendants().Where(e => e.Name.LocalName == "Alignment"))
        {
            if (Child(al, "CoordGeom") is not { } geom) continue;
            var name = (string?)al.Attribute("name") is { Length: > 0 } n ? n : $"路線{list.Count + 1}";
            list.Add(new LandXmlAlignment(
                name,
                Num(al.Attribute("staStart")) ?? 0,
                ReadPitch(al),
                ToIps(name, geom, ReadAlignPis(al)),
                Num(al.Attribute("length")) ?? double.NaN));
        }
        if (list.Count == 0) throw new AlignmentException("LandXML に平面線形 (Alignment/CoordGeom) がありません。");
        return list;
    }

    // ---- 要素 ----

    private sealed record Elem(string Kind, string? StartName, string? EndName, Vec2 Start, Vec2 End,
        double DirStart, double DirEnd, double RadiusStart, double RadiusEnd, double Length, double A)
    {
        public bool IsLine => Kind == "Line";
    }

    private static List<IntersectionPoint> ToIps(string alignmentName, XElement geom, List<(string Name, Vec2 Point)> alignPis)
    {
        var elems = new List<Elem>();
        foreach (var e in geom.Elements())
        {
            var prevDir = elems.Count > 0 ? elems[^1].DirEnd : (double?)null;
            if (e.Name.LocalName == "Line" && ReadLine(e).Length < 1e-6) continue; // 長さ 0 の直線は無視
            elems.Add(e.Name.LocalName switch
            {
                "Line" => ReadLine(e),
                "Curve" => ReadCurve(e),
                "Spiral" => ReadSpiral(e, prevDir),
                var other => throw new AlignmentException($"{alignmentName}: 対応していない線形要素 {other} があります。"),
            });
        }
        if (elems.Count == 0) throw new AlignmentException($"{alignmentName}: 線形要素がありません。");

        var ips = new List<IntersectionPoint>
        {
            new(PiName(alignPis, elems[0].Start) ?? elems[0].StartName ?? "BP", elems[0].Start),
        };

        var group = new List<Elem>();
        for (var i = 0; i < elems.Count; i++)
        {
            var e = elems[i];
            if (e.IsLine)
            {
                // 直線どうしの継ぎ目は曲線の無い折れ点 (R = 0)
                if (i > 0 && elems[i - 1].IsLine)
                    ips.Add(new(PiName(alignPis, e.Start) ?? e.StartName ?? $"IP.{ips.Count}", e.Start));
                continue;
            }
            group.Add(e);
            var next = i + 1 < elems.Count ? elems[i + 1] : null;
            // 曲線のまとまりの終わり: 直線の手前、半径∞に戻ったところ、円曲線どうしの継ぎ目
            var close = next == null || next.IsLine || double.IsInfinity(e.RadiusEnd) || double.IsInfinity(next.RadiusStart)
                || (e.Kind == "Curve" && next.Kind == "Curve");
            if (!close) continue;
            ips.Add(GroupToIp(alignmentName, group, alignPis, ips.Count));
            group = [];
        }

        var last = elems[^1];
        ips.Add(new(PiName(alignPis, last.End) ?? last.EndName ?? "EP", last.End));
        return ips;
    }

    /// <summary>曲線のまとまり [クロソイド] 円曲線 [クロソイド] を 1 つの IP にする。</summary>
    private static IntersectionPoint GroupToIp(string alignmentName, List<Elem> g, List<(string Name, Vec2 Point)> alignPis, int index)
    {
        var first = g[0];
        var last = g[^1];
        var where = $"{alignmentName} {first.StartName ?? ""}〜{last.EndName ?? ""}";

        Elem? spiralIn = null, curve = null, spiralOut = null;
        foreach (var e in g)
        {
            if (e.Kind == "Spiral" && double.IsInfinity(e.RadiusStart) && curve == null && spiralIn == null) spiralIn = e;
            else if (e.Kind == "Curve" && curve == null && spiralOut == null) curve = e;
            else if (e.Kind == "Spiral" && double.IsInfinity(e.RadiusEnd) && spiralOut == null) spiralOut = e;
            else throw new AlignmentException($"{where}: 卵形クロソイドなど、IP 法で表せない線形は読み込めません。");
        }

        var radius = curve?.RadiusStart ?? (spiralIn?.RadiusEnd is double r1 && !double.IsInfinity(r1) ? r1 : spiralOut?.RadiusStart ?? 0);
        if (double.IsInfinity(radius) || radius <= 0) throw new AlignmentException($"{where}: 曲線の半径が読み取れません。");
        if (spiralIn != null && Math.Abs(spiralIn.RadiusEnd - radius) > 1e-3 || spiralOut != null && Math.Abs(spiralOut.RadiusStart - radius) > 1e-3)
            throw new AlignmentException($"{where}: クロソイドと円曲線の半径が一致しません。");

        var ip = Intersect(first.Start, first.DirStart, last.End, last.DirEnd)
            ?? throw new AlignmentException($"{where}: 前後の接線が平行で IP が求まりません。");
        return new IntersectionPoint(PiName(alignPis, ip) ?? $"IP.{index}", ip, radius, spiralIn?.A ?? 0, spiralOut?.A ?? 0);
    }

    private static Elem ReadLine(XElement e)
    {
        var s = Pt(e, "Start");
        var t = Pt(e, "End");
        var dir = (t - s).Angle;
        return new Elem("Line", Name(e, "Start"), Name(e, "End"), s, t, dir, dir, Inf, Inf, s.DistanceTo(t), 0);
    }

    private static Elem ReadCurve(XElement e)
    {
        var s = Pt(e, "Start");
        var t = Pt(e, "End");
        var c = Pt(e, "Center");
        var turn = Rot(e);
        var r = Num(e.Attribute("radius")) ?? s.DistanceTo(c);
        // 接線方向 = 中心→点 を回転方向に 90° 回したもの
        double Tangent(Vec2 p) => (p - c).Angle + turn * Math.PI / 2;
        return new Elem("Curve", Name(e, "Start"), Name(e, "End"), s, t, Tangent(s), Tangent(t), r, r,
            Num(e.Attribute("length")) ?? double.NaN, 0);
    }

    private static Elem ReadSpiral(XElement e, double? prevDir)
    {
        var s = Pt(e, "Start");
        var t = Pt(e, "End");
        var r0 = Radius(e.Attribute("radiusStart"));
        var r1 = Radius(e.Attribute("radiusEnd"));
        var len = Num(e.Attribute("length")) ?? throw new AlignmentException("クロソイドの長さ (length) がありません。");
        var turn = Rot(e);

        double dirStart, dirEnd;
        if (Child(e, "PI") is not null)
        {
            var pi = Pt(e, "PI");
            dirStart = (pi - s).Angle;
            dirEnd = (t - pi).Angle;
        }
        else
        {
            // PI が無ければ 接線角 τ = L / 2R から求める
            var tau = len / (2 * Math.Min(r0, r1));
            dirStart = prevDir ?? (t - s).Angle - turn * tau / 3;
            dirEnd = dirStart + turn * tau;
        }

        var spiralType = (string?)e.Attribute("spiType");
        if (spiralType != null && spiralType != "clothoid") throw new AlignmentException($"クロソイド以外の緩和曲線 ({spiralType}) には対応していません。");

        var a = e.Descendants().FirstOrDefault(p => p.Name.LocalName == "Property" && (string?)p.Attribute("label") == "A") is { } prop
            ? Num(prop.Attribute("value")) : null;
        a ??= Math.Sqrt(Math.Min(r0, r1) * len);
        return new Elem("Spiral", Name(e, "Start"), Name(e, "End"), s, t, dirStart, dirEnd, r0, r1, len, a.Value);
    }

    // ---- 補助 ----

    private static Vec2? Intersect(Vec2 p, double dirP, Vec2 q, double dirQ)
    {
        var u = Vec2.FromAngle(dirP);
        var v = Vec2.FromAngle(dirQ);
        var den = u.Cross(v);
        if (Math.Abs(den) < 1e-12) return null;
        return p + u * ((q - p).Cross(v) / den);
    }

    private static List<(string, Vec2)> ReadAlignPis(XElement al) =>
        Child(al, "AlignPIs") is { } pis
            ? pis.Descendants().Where(e => e.Name.LocalName == "PI").Select(e => ((string?)e.Attribute("name") ?? "", ParsePoint(e.Value))).ToList()
            : [];

    /// <summary>ファイルの AlignPIs に同じ位置の点があればその名前。</summary>
    private static string? PiName(List<(string Name, Vec2 Point)> pis, Vec2 p) =>
        pis.Where(x => x.Name.Length > 0 && x.Point.DistanceTo(p) < 0.01).Select(x => x.Name).FirstOrDefault();

    private static double? ReadPitch(XElement al) =>
        al.Elements().Where(e => e.Name.LocalName == "Feature" && (string?)e.Attribute("name") == "Interval")
            .SelectMany(e => e.Elements()).Where(p => (string?)p.Attribute("label") == "main")
            .Select(p => Num(p.Attribute("value"))).FirstOrDefault(v => v > 0);

    private static XElement? Child(XElement e, string localName) => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName);

    private static string? Name(XElement e, string child) => (string?)Child(e, child)?.Attribute("name");

    private static Vec2 Pt(XElement e, string child) =>
        ParsePoint(Child(e, child)?.Value ?? throw new AlignmentException($"{e.Name.LocalName} に {child} がありません。"));

    /// <summary>「北 東 [標高]」→ CAD 座標。</summary>
    private static Vec2 ParsePoint(string text)
    {
        var f = text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2
            || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var north)
            || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var east))
            throw new AlignmentException($"座標「{text}」が読み取れません。");
        return Vec2.FromSurvey(north, east);
    }

    /// <summary>rot="cw" は右回り (-1)、"ccw" は左回り (+1)。</summary>
    private static int Rot(XElement e) => (string?)e.Attribute("rot") switch
    {
        "cw" => -1,
        "ccw" => 1,
        _ => throw new AlignmentException($"{e.Name.LocalName} の回転方向 (rot) がありません。"),
    };

    private static double Radius(XAttribute? a) =>
        a == null || a.Value.Trim().Equals("INF", StringComparison.OrdinalIgnoreCase) ? Inf : Num(a) is double r && r > 0 ? r : Inf;

    private static double? Num(XAttribute? a) =>
        a != null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
