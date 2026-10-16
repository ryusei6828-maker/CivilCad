using System.Globalization;
using System.Xml.Linq;

namespace CivilCad.Alignment;

/// <summary>横断の 1 点 (中心からの離れ: 右が +、左が −)。Code は計画横断の点の名前 (無ければ null)。</summary>
public readonly record struct SectionPoint(double Offset, double Elevation, string? Code = null);

/// <summary>横断の 1 本の線。Design = true は計画 (DesignCrossSectSurf)、false は現況地盤など (CrossSectSurf)。</summary>
public sealed record SectionLine(string Name, bool Design, IReadOnlyList<SectionPoint> Points);

/// <summary>1 測点の横断。</summary>
public sealed record SectionAtStation(double Station, string? Name, IReadOnlyList<SectionLine> Lines);

/// <summary>縦断の地盤線など (ProfSurf)。</summary>
public sealed record ProfileLine(string Name, IReadOnlyList<(double Station, double Elevation)> Points);

/// <summary>
/// LandXML の 1 路線の縦断・横断 (縦横断図を描くのに使う)。
/// CivilCadPoint (点群) の書き出し: ProfSurf (現況地盤) + CrossSectSurf。CivilCad3D の書き出し: ProfAlign + DesignCrossSectSurf。
/// </summary>
public sealed record RouteSections(
    string Name,
    double StartStation,
    double EndStation,
    double Pitch,
    IReadOnlyList<ProfileLine> GroundProfiles,
    VerticalAlignment? Design,
    string? DesignName,
    IReadOnlyList<SectionAtStation> Sections,
    string? Source)
{
    /// <summary>測点の表記 (No.3+5.000)。</summary>
    public string Label(double station) => Station.Format(station, Pitch);

    /// <summary>縦断も横断も無い (図にするものが無い)。</summary>
    public bool IsEmpty => GroundProfiles.Count == 0 && Design == null && Sections.Count == 0;
}

/// <summary>LandXML (J-LandXML) から路線ごとの縦断 (ProfSurf・ProfAlign) と横断 (CrossSects) を読む。</summary>
public static class LandXmlSectionReader
{
    public static List<RouteSections> Read(string path)
    {
        var list = Parse(XDocument.Load(path));
        return list.Select(r => r with { Source = path }).ToList();
    }

    public static List<RouteSections> Parse(XDocument xml)
    {
        var profiles = LandXmlProfileReader.Parse(xml);
        var result = new List<RouteSections>();
        var alignments = xml.Descendants().Where(e => e.Name.LocalName == "Alignment").ToList();
        foreach (var al in alignments)
        {
            var name = (string?)al.Attribute("name") is { Length: > 0 } n ? n : $"路線{result.Count + 1}";
            var start = Num(al.Attribute("staStart")) ?? 0;
            var length = Num(al.Attribute("length"));
            if (length is not > 0)
            {
                // 長さが書いていなければ平面線形から求める
                try
                {
                    var read = LandXmlReader.Parse(new XDocument(new XElement(xml.Root!.Name, new XElement(al))));
                    length = HorizontalAlignment.FromIps(read[0].Ips, start).Length;
                }
                catch (AlignmentException) { length = 0; }
            }

            var ground = new List<ProfileLine>();
            foreach (var ps in al.Descendants().Where(e => e.Name.LocalName == "ProfSurf"))
            {
                var pts = Pairs(Child(ps, "PntList2D")?.Value).OrderBy(p => p.A).ToList();
                if (pts.Count >= 2) ground.Add(new ProfileLine((string?)ps.Attribute("name") ?? "現況地盤", pts));
            }

            var p = profiles.FirstOrDefault(x => x.AlignmentName == name) ?? (alignments.Count == 1 ? profiles.FirstOrDefault() : null);
            VerticalAlignment? design = null;
            if (p != null)
            {
                try { design = new VerticalAlignment(p.Ips); }
                catch (AlignmentException) { p = null; }
            }

            var sections = new List<SectionAtStation>();
            foreach (var cs in al.Descendants().Where(e => e.Name.LocalName == "CrossSect"))
            {
                if (Num(cs.Attribute("sta")) is not { } sta) continue;
                var lines = new List<SectionLine>();
                foreach (var s in cs.Elements())
                {
                    switch (s.Name.LocalName)
                    {
                        case "CrossSectSurf":
                        {
                            var pts = Pairs(Child(s, "PntList2D")?.Value).Select(q => new SectionPoint(q.A, q.B)).OrderBy(q => q.Offset).ToList();
                            if (pts.Count >= 2) lines.Add(new SectionLine((string?)s.Attribute("name") ?? "現況地盤", false, pts));
                            break;
                        }
                        case "DesignCrossSectSurf":
                        {
                            var pts = new List<SectionPoint>();
                            // 計画の点は並び順のまま (垂直な面などで離れが戻ることがある)
                            foreach (var cp in s.Elements().Where(e => e.Name.LocalName == "CrossSectPnt"))
                                foreach (var q in Pairs(cp.Value).Take(1))
                                    pts.Add(new SectionPoint(q.A, q.B, (string?)cp.Attribute("code")));
                            if (pts.Count >= 2) lines.Add(new SectionLine((string?)s.Attribute("name") ?? "計画", true, pts));
                            break;
                        }
                    }
                }
                sections.Add(new SectionAtStation(sta, (string?)cs.Attribute("name"), lines));
            }

            var pitch = al.Elements().Where(e => e.Name.LocalName == "Feature" && (string?)e.Attribute("name") == "Interval")
                .SelectMany(e => e.Elements()).Where(x => (string?)x.Attribute("label") == "main")
                .Select(x => Num(x.Attribute("value"))).FirstOrDefault(v => v > 0) ?? Station.DefaultPitch;

            result.Add(new RouteSections(name, start, start + (length ?? 0), pitch, ground, design, p?.Name,
                [.. sections.OrderBy(s => s.Station)], null));
        }
        return result;
    }

    private static XElement? Child(XElement e, string localName) => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName);

    /// <summary>「a b a b …」の並び (空白・改行・カンマ区切り)。</summary>
    private static IEnumerable<(double A, double B)> Pairs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var f = text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < f.Length; i += 2)
            if (double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                double.TryParse(f[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                yield return (a, b);
    }

    private static double? Num(XAttribute? a) =>
        a != null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
