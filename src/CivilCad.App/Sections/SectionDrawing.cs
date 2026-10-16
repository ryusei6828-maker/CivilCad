using System.Globalization;
using CivilCad.Alignment;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Sections;

/// <summary>縦断図・横断図の縮尺。</summary>
/// <param name="ProfileHorizontal">縦断図の横 (距離) の縮尺の分母。</param>
/// <param name="ProfileVertical">縦断図の縦 (高さ) の縮尺の分母。</param>
/// <param name="Section">横断図の縮尺の分母 (縦横同じ)。</param>
/// <param name="Columns">横断図を横に並べる数。</param>
/// <param name="DatumStep">横断図の基準高 (DL) の刻み (m)。DL はこの倍数に切り下げる。</param>
public sealed record SectionScales(double ProfileHorizontal = 1000, double ProfileVertical = 100, double Section = 100, int Columns = 3,
    double DatumStep = 5)
{
    /// <summary>横断図の基準高: いちばん低い所より 1m 以上下の、刻みの倍数。</summary>
    public double SectionDatum(double minElevation)
    {
        var step = DatumStep > 0 ? DatumStep : 1;
        return Math.Floor((minElevation - 1) / step) * step;
    }

    /// <summary>縦断図の高さの強調 (横の縮尺 / 縦の縮尺)。</summary>
    public double Exaggeration => ProfileHorizontal / ProfileVertical;
}

/// <summary>
/// 路線の縦断・横断 (LandXML から読んだもの) から、縦断図・横断図を CivilCad の図形で描く。
/// 図面の座標は m。縦断図は横 = 追加距離、縦 = (標高 − 基準高) × 強調。横断図は縦横同じ。
/// 文字は用紙の上で 2.5mm (シートの縮尺で m に直す)。
/// </summary>
public static class SectionDrawing
{
    private static readonly RgbColor GroundColor = new(205, 133, 63);
    private static readonly RgbColor DesignColor = new(255, 64, 64);
    private static readonly RgbColor FrameColor = RgbColor.White;
    private static readonly RgbColor GridColor = new(128, 128, 128);
    private static readonly RgbColor CenterColor = new(255, 96, 96);

    // ───────────────────────────── 縦断図 ─────────────────────────────

    /// <summary>空のシート doc に縦断図を描く (縮尺も設定する)。描いた図形の数。</summary>
    public static int DrawProfile(CadDocument doc, RouteSections r, SectionScales scales)
    {
        doc.ScaleDenominator = scales.ProfileHorizontal;
        var th = doc.PaperMmToMeters(2.5);
        var ex = scales.Exaggeration;
        var lGround = doc.GetOrCreateLayer("現況地盤", GroundColor);
        var lDesign = doc.GetOrCreateLayer("計画縦断", DesignColor);
        var lFrame = doc.GetOrCreateLayer("枠", FrameColor);
        var lText = doc.GetOrCreateLayer("文字", FrameColor);
        var lGrid = doc.GetOrCreateLayer("補助線", GridColor);
        var n0 = doc.Entities.Count;

        var start = r.StartStation;
        var end = r.EndStation > r.StartStation ? r.EndStation
            : Math.Max(r.GroundProfiles.SelectMany(g => g.Points).Select(p => p.Station).DefaultIfEmpty(start).Max(),
                r.Sections.Select(s => s.Station).DefaultIfEmpty(start).Max());
        var zs = r.GroundProfiles.SelectMany(g => g.Points.Select(p => p.Elevation)).ToList();
        if (r.Design != null)
            for (var s = start; s <= end; s += Math.Max((end - start) / 200, 0.5)) zs.Add(r.Design.ElevationAt(s));
        if (zs.Count == 0) throw new InvalidOperationException("縦断 (地盤線・計画縦断) がありません。");
        var dl = Math.Floor(zs.Min() - 2);
        var top = Math.Ceiling(zs.Max() + 2);
        double Y(double z) => (z - dl) * ex;
        Vec2 P(double s, double z) => new(s, Y(z));

        // 地盤線 (点のあきが大きい所で切る) と計画線
        foreach (var g in r.GroundProfiles)
        {
            var gap = Gap(g.Points.Select(p => p.Station).ToList());
            foreach (var run in Runs(g.Points, p => p.Station, gap))
                doc.Entities.Add(new PolylineEntity(lGround, run.Select(p => P(p.Station, p.Elevation))));
        }
        if (r.Design is { } d)
        {
            var pts = new List<Vec2>();
            for (var s = start; ; s = Math.Min(s + Math.Max((end - start) / 1000, 0.5), end))
            {
                pts.Add(P(s, d.ElevationAt(s)));
                if (s >= end) break;
            }
            doc.Entities.Add(new PolylineEntity(lDesign, pts));
            foreach (var ip in d.Ips)
                doc.Entities.Add(Text(lDesign, P(ip.Station, ip.Elevation) + new Vec2(0, th * 1.5), $"VIP {ip.Elevation:F3}", th, TextHAlign.Center, TextVAlign.Bottom));
        }

        // 高さの目盛と基準線
        var zStep = NiceStep((top - dl) / 8);
        doc.Entities.Add(new LineEntity(lFrame, new Vec2(start, 0), new Vec2(start, Y(top))));
        doc.Entities.Add(new LineEntity(lFrame, new Vec2(start, 0), new Vec2(end, 0)));
        for (var z = dl; z <= top + 1e-9; z += zStep)
        {
            doc.Entities.Add(new LineEntity(lGrid, P(start, z), P(end, z)));
            doc.Entities.Add(Text(lText, new Vec2(start - th * 1.5, Y(z)), z.ToString("0.0", CultureInfo.InvariantCulture), th, TextHAlign.Right, TextVAlign.Middle));
        }
        doc.Entities.Add(Text(lText, new Vec2(start, -th * 0.5), $"DL={dl:F3}", th, TextHAlign.Right, TextVAlign.Top));

        // 下の表
        var rows = new List<string> { "測点", "追加距離", "地盤高" };
        if (r.Design != null) { rows.Add("計画高"); rows.Add("切土・盛土"); }
        var titles = r.Sections.Select(s => Title(r, s)).ToList();
        var longest = titles.Select(t => t.Length).DefaultIfEmpty(8).Max();
        var rowHs = rows.Select((_, i) => doc.PaperMmToMeters(i == 0 ? Math.Max(24, longest * 2.5 * 0.9 + 6) : 24)).ToArray();
        var rowTops = new double[rows.Count + 1];
        rowTops[0] = -th * 3;
        for (var i = 0; i < rows.Count; i++) rowTops[i + 1] = rowTops[i] - rowHs[i];
        var labelW = doc.PaperMmToMeters(30);
        foreach (var y in rowTops) doc.Entities.Add(new LineEntity(lFrame, new Vec2(start - labelW, y), new Vec2(end, y)));
        doc.Entities.Add(new LineEntity(lFrame, new Vec2(start - labelW, rowTops[0]), new Vec2(start - labelW, rowTops[^1])));
        doc.Entities.Add(new LineEntity(lFrame, new Vec2(end, rowTops[0]), new Vec2(end, rowTops[^1])));
        for (var i = 0; i < rows.Count; i++)
            doc.Entities.Add(Text(lText, new Vec2(start - labelW / 2, (rowTops[i] + rowTops[i + 1]) / 2), rows[i], th, TextHAlign.Center, TextVAlign.Middle));

        // 測点ごと (横断がある測点。無ければ No. ごと)
        var stations = r.Sections.Count > 0
            ? r.Sections.Select((s, i) => (s.Station, titles[i])).ToList()
            : NoStations(r, start, end);
        foreach (var (s, title) in stations)
        {
            var g = GroundAt(r, s);
            var dz = r.Design?.ElevationAt(s);
            var yTop = Y(Math.Max(g ?? dl, dz ?? dl));
            doc.Entities.Add(new LineEntity(lGrid, new Vec2(s, yTop), new Vec2(s, rowTops[^1])));
            var cells = new List<string>
            {
                title,
                (s - start).ToString("F2", CultureInfo.InvariantCulture),
                g?.ToString("F3", CultureInfo.InvariantCulture) ?? "-",
            };
            if (r.Design != null)
            {
                cells.Add(dz?.ToString("F3", CultureInfo.InvariantCulture) ?? "-");
                cells.Add(dz is { } dv && g is { } gv ? $"{(gv > dv ? "C" : "F")} {Math.Abs(gv - dv):F3}" : "-");
            }
            for (var i = 0; i < cells.Count; i++)
                doc.Entities.Add(Text(lText, new Vec2(s + th * 0.6, (rowTops[i] + rowTops[i + 1]) / 2), cells[i], th, TextHAlign.Center, TextVAlign.Middle, Math.PI / 2));
        }

        var head = $"縦断図  {r.Name}   H=1:{scales.ProfileHorizontal:0}  V=1:{scales.ProfileVertical:0}";
        if (r.GroundProfiles.Count > 0) head += $"   地盤線: {string.Join("・", r.GroundProfiles.Select(g => g.Name))}";
        doc.Entities.Add(Text(lText, new Vec2(start, Y(top) + th * 3), head, th * 2, TextHAlign.Left, TextVAlign.Bottom));
        return doc.Entities.Count - n0;
    }

    // ───────────────────────────── 横断図 ─────────────────────────────

    /// <summary>
    /// 空のシート doc に横断図を並べて描く (上から下、左から右)。描いた横断の数。
    /// 横断ごとに 基準線 (DL は刻みの倍数)・中心線・地盤線・計画線・計画高 (FH)・見出し (測点と中心の地盤高 GH) を描く。
    /// </summary>
    public static int DrawSections(CadDocument doc, RouteSections r, SectionScales scales)
    {
        doc.ScaleDenominator = scales.Section;
        var th = doc.PaperMmToMeters(2.5);
        var lGround = doc.GetOrCreateLayer("現況地盤", GroundColor);
        var lDesign = doc.GetOrCreateLayer("計画", DesignColor);
        var lFrame = doc.GetOrCreateLayer("枠", FrameColor);
        var lText = doc.GetOrCreateLayer("文字", FrameColor);
        var lCenter = doc.GetOrCreateLayer("中心線", CenterColor);

        var sections = r.Sections.Where(s => s.Lines.Count > 0).ToList();
        if (sections.Count == 0) throw new InvalidOperationException("横断がありません。");
        var allPts = sections.SelectMany(s => s.Lines.SelectMany(l => l.Points)).ToList();
        var left = Math.Ceiling(-allPts.Min(p => p.Offset));
        var right = Math.Ceiling(allPts.Max(p => p.Offset));
        left = Math.Max(left, 1);
        right = Math.Max(right, 1);
        // 1 つの横断の高さ: 基準高 (DL) から見出しまで + あき
        var heights = sections.Select(s => Range(s, r)).Max(x => x.Max + 2 - scales.SectionDatum(x.Min));
        var cellW = left + right + doc.PaperMmToMeters(40);
        var cellH = Math.Ceiling(heights) + doc.PaperMmToMeters(20);
        var perColumn = (int)Math.Ceiling(sections.Count / (double)Math.Max(1, scales.Columns));
        var topY = 0.0;

        for (var k = 0; k < sections.Count; k++)
        {
            var sec = sections[k];
            var ox = k / perColumn * cellW + left + doc.PaperMmToMeters(20);
            var oy = -(k % perColumn) * cellH;
            var (minZ, maxZ) = Range(sec, r);
            var dl = scales.SectionDatum(minZ);
            Vec2 P(double off, double z) => new(ox + off, oy + (z - dl));

            doc.Entities.Add(new LineEntity(lFrame, P(-left, dl), P(right, dl)));
            doc.Entities.Add(new LineEntity(lCenter, P(0, dl), P(0, maxZ + 1.5)) { LineType = LineType.DashDot });
            doc.Entities.Add(Text(lText, P(-left, dl) + new Vec2(0, th * 0.5), $"DL={dl:F3}", th, TextHAlign.Left, TextVAlign.Bottom));

            foreach (var line in sec.Lines)
            {
                var layer = line.Design ? lDesign : lGround;
                if (line.Design)
                    doc.Entities.Add(new PolylineEntity(layer, line.Points.Select(p => P(p.Offset, p.Elevation))));
                else
                    foreach (var run in Runs(line.Points, p => p.Offset, Gap(line.Points.Select(p => p.Offset).ToList())))
                        doc.Entities.Add(new PolylineEntity(layer, run.Select(p => P(p.Offset, p.Elevation))));
            }

            // 中心の計画高 (計画縦断から)
            var fh = r.Design?.ElevationAt(sec.Station);
            if (fh is { } f)
            {
                doc.Entities.Add(new LineEntity(lDesign, P(-1, f), P(1, f)));
                doc.Entities.Add(Text(lDesign, P(0, f) + new Vec2(th * 0.5, th * 0.5), $"FH={f:F3}", th, TextHAlign.Left, TextVAlign.Bottom));
            }

            var ground = sec.Lines.FirstOrDefault(l => !l.Design);
            var gh = ground != null && ElevationAt(ground.Points, 0, Gap(ground.Points.Select(p => p.Offset).ToList())) is { } c ? $"   GH={c:F3}" : "";
            var title = Title(r, sec) + gh;
            doc.Entities.Add(Text(lText, P(0, maxZ + 2), title, th * 1.4, TextHAlign.Center, TextVAlign.Bottom));
            topY = Math.Max(topY, P(0, maxZ + 2).Y + th * 1.4);
        }

        var lines = sections.SelectMany(s => s.Lines).Select(l => l.Name).Distinct();
        doc.Entities.Add(Text(lText, new Vec2(0, topY + th * 3),
            $"横断図  {r.Name}   S=1:{scales.Section:0}   線: {string.Join("・", lines)}", th * 2, TextHAlign.Left, TextVAlign.Bottom));
        return sections.Count;
    }

    // ───────────────────────────── 補助 ─────────────────────────────

    /// <summary>測点の見出し (ファイルの名前があればそれ、無ければ No. の表記)。</summary>
    public static string Title(RouteSections r, SectionAtStation s) => s.Name is { Length: > 0 } n ? n : r.Label(s.Station);

    /// <summary>縦断の地盤高 (最初の地盤線。点の間は直線、あきが大きい所は null)。</summary>
    public static double? GroundAt(RouteSections r, double station)
    {
        if (r.GroundProfiles.Count == 0) return null;
        var pts = r.GroundProfiles[0].Points;
        var gap = Gap(pts.Select(p => p.Station).ToList());
        for (var i = 0; i < pts.Count; i++)
        {
            if (Math.Abs(pts[i].Station - station) < 1e-6) return pts[i].Elevation;
            if (i + 1 < pts.Count && pts[i].Station < station && pts[i + 1].Station > station)
            {
                var (s0, z0) = pts[i];
                var (s1, z1) = pts[i + 1];
                if (s1 - s0 > gap + 1e-9) return null;
                return z0 + (z1 - z0) * (station - s0) / (s1 - s0);
            }
        }
        return null;
    }

    public static double? ElevationAt(IReadOnlyList<SectionPoint> pts, double offset, double gap)
    {
        for (var i = 0; i < pts.Count; i++)
        {
            if (Math.Abs(pts[i].Offset - offset) < 1e-9) return pts[i].Elevation;
            if (i + 1 < pts.Count && pts[i].Offset < offset && pts[i + 1].Offset > offset)
            {
                var a = pts[i];
                var b = pts[i + 1];
                if (b.Offset - a.Offset > gap + 1e-9) return null;
                return a.Elevation + (b.Elevation - a.Elevation) * (offset - a.Offset) / (b.Offset - a.Offset);
            }
        }
        return null;
    }

    private static (double Min, double Max) Range(SectionAtStation s, RouteSections r)
    {
        var zs = s.Lines.SelectMany(l => l.Points).Select(p => p.Elevation).ToList();
        if (r.Design != null) zs.Add(r.Design.ElevationAt(s.Station));
        return (zs.Min(), zs.Max());
    }

    /// <summary>点が無いとみなすあき: 点の間隔の中央値の 3 倍。</summary>
    private static double Gap(List<double> xs)
    {
        if (xs.Count < 2) return double.PositiveInfinity;
        var d = xs.Zip(xs.Skip(1), (a, b) => b - a).Where(v => v > 1e-9).OrderBy(v => v).ToList();
        return d.Count == 0 ? double.PositiveInfinity : d[d.Count / 2] * 3;
    }

    private static IEnumerable<List<T>> Runs<T>(IReadOnlyList<T> pts, Func<T, double> key, double gap)
    {
        var run = new List<T>();
        foreach (var p in pts)
        {
            if (run.Count > 0 && key(p) - key(run[^1]) > gap + 1e-9)
            {
                if (run.Count >= 2) yield return run;
                run = [];
            }
            run.Add(p);
        }
        if (run.Count >= 2) yield return run;
    }

    private static List<(double, string)> NoStations(RouteSections r, double start, double end)
    {
        var list = new List<(double, string)>();
        for (var s = Math.Ceiling(start / r.Pitch) * r.Pitch; s <= end + 1e-6; s += r.Pitch) list.Add((s, r.Label(s)));
        return list;
    }

    private static TextEntity Text(Layer layer, Vec2 at, string text, double height, TextHAlign h, TextVAlign v, double rotation = 0) =>
        new(layer, at, text, height, rotation) { HAlign = h, VAlign = v };

    private static double NiceStep(double raw)
    {
        var e = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-6))));
        foreach (var m in new[] { 1, 2, 5, 10 })
            if (m * e >= raw) return m * e;
        return 10 * e;
    }
}
