using System.Globalization;
using System.Xml.Linq;

namespace CivilCad.Alignment;

/// <summary>LandXML から読み込んだ縦断線形。</summary>
public sealed record LandXmlProfile(string AlignmentName, string Name, IReadOnlyList<VerticalIp> Ips);

/// <summary>
/// LandXML (J-LandXML) の縦断線形 (Alignment/Profile/ProfAlign) を読む。
/// PVI は変化点、ParaCurve は放物線の縦断曲線 (length = 曲線長)、CircCurve は length を曲線長として放物線で近似する。
/// </summary>
public static class LandXmlProfileReader
{
    public static List<LandXmlProfile> Read(string path) => Parse(XDocument.Load(path));

    public static List<LandXmlProfile> Parse(XDocument xml)
    {
        var list = new List<LandXmlProfile>();
        foreach (var al in xml.Descendants().Where(e => e.Name.LocalName == "Alignment"))
        {
            var alName = (string?)al.Attribute("name") ?? "";
            foreach (var pa in al.Descendants().Where(e => e.Name.LocalName == "ProfAlign"))
            {
                var ips = new List<VerticalIp>();
                foreach (var e in pa.Elements())
                {
                    var kind = e.Name.LocalName;
                    if (kind is not ("PVI" or "ParaCurve" or "CircCurve" or "UnsymParaCurve")) continue;
                    var f = e.Value.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 2
                        || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sta)
                        || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var elev))
                        throw new AlignmentException($"縦断の {kind}「{e.Value}」が読み取れません。");
                    var length = kind == "PVI" ? 0 : Num(e.Attribute("length"))
                        ?? (Num(e.Attribute("lengthIn")) ?? 0) + (Num(e.Attribute("lengthOut")) ?? 0);
                    ips.Add(new VerticalIp(sta, elev, length));
                }
                if (ips.Count >= 2)
                    list.Add(new LandXmlProfile(alName, (string?)pa.Attribute("name") ?? alName, ips));
            }
        }
        return list;
    }

    private static double? Num(XAttribute? a) =>
        a != null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
