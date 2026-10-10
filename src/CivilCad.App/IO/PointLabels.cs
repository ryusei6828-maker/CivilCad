using System.Globalization;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.IO;

/// <summary>
/// 点名・標高を持てない形式 (SFC / P21) で、登録した座標の点名と標高を文字として残す。
/// 保存: 点の右上に点名 (レイヤ「座標-点名」)、右下に「H=標高」(レイヤ「座標-標高」) の文字を書く (用紙上 2.5mm)。
/// 読み込み: そのレイヤの文字が点のすぐ横 (書いた位置) にあれば、点の点名・標高に戻して文字は消す。
/// </summary>
public static class PointLabels
{
    public const string NameLayer = "座標-点名";
    public const string ElevationLayer = "座標-標高";

    /// <summary>文字の高さ (用紙上の mm)。</summary>
    public const double TextHeightMm = 2.5;

    /// <summary>点から文字までの離れ (用紙上の mm)。</summary>
    private const double GapMm = 1.0;

    /// <summary>読み込みで「点の横の文字」とみなす位置のずれ (用紙上の mm)。</summary>
    private const double ToleranceMm = 0.5;

    private const string ElevationPrefix = "H=";

    /// <summary>点名の文字の左下の位置。</summary>
    private static Vec2 NameAnchor(CadDocument doc, Vec2 p) => p + new Vec2(doc.PaperMmToMeters(GapMm), doc.PaperMmToMeters(GapMm * 0.5));

    /// <summary>標高の文字の左下の位置 (点名の下)。</summary>
    private static Vec2 ElevationAnchor(CadDocument doc, Vec2 p) =>
        p + new Vec2(doc.PaperMmToMeters(GapMm), -doc.PaperMmToMeters(GapMm * 0.5 + TextHeightMm));

    /// <summary>保存用の点名・標高のレイヤ (保存 1 回につき 1 つずつ作って使い回す)。</summary>
    public static (Layer Name, Layer Elevation) CreateLayers() => (new Layer(NameLayer, RgbColor.White), new Layer(ElevationLayer, RgbColor.White));

    /// <summary>保存用: 点と、点名・標高の文字 (色は点の色)。点名も標高も無い点はそのまま。</summary>
    public static IEnumerable<Entity> Expand(CadDocument doc, PointEntity p, (Layer Name, Layer Elevation) layers)
    {
        yield return p;
        var h = doc.PaperMmToMeters(TextHeightMm);
        if (!string.IsNullOrWhiteSpace(p.Name))
            yield return new TextEntity(layers.Name, NameAnchor(doc, p.Position), p.Name, h) { Color = p.EffectiveColor };
        if (p.Z is double z)
            yield return new TextEntity(layers.Elevation, ElevationAnchor(doc, p.Position),
                ElevationPrefix + z.ToString("F3", CultureInfo.InvariantCulture), h) { Color = p.EffectiveColor };
    }

    /// <summary>
    /// 読み込み後: 点名・標高のレイヤの文字のうち、点のすぐ横にあるものを点の点名・標高に戻して消す。戻した点の数を返す。
    /// 点に合わない文字は文字のまま残す。
    /// </summary>
    public static int Fold(CadDocument doc)
    {
        var points = doc.Entities.OfType<PointEntity>().ToList();
        if (points.Count == 0) return 0;
        var tol = doc.PaperMmToMeters(ToleranceMm);
        var used = new HashSet<Entity>();
        var restored = new HashSet<PointEntity>();
        foreach (var t in doc.Entities.OfType<TextEntity>().ToList())
        {
            var isName = t.Layer.Name == NameLayer;
            var isZ = t.Layer.Name == ElevationLayer;
            if (!isName && !isZ) continue;
            var p = points.MinBy(q => (isName ? NameAnchor(doc, q.Position) : ElevationAnchor(doc, q.Position)).DistanceTo(t.Position));
            if (p == null || (isName ? NameAnchor(doc, p.Position) : ElevationAnchor(doc, p.Position)).DistanceTo(t.Position) > tol) continue;
            if (isName)
            {
                p.Name = t.Text;
            }
            else
            {
                var s = t.Text.Trim();
                if (s.StartsWith(ElevationPrefix, StringComparison.OrdinalIgnoreCase)) s = s[ElevationPrefix.Length..];
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;
                p.Z = z;
            }
            used.Add(t);
            restored.Add(p);
        }
        if (used.Count == 0) return 0;
        doc.Entities.RemoveAll(used.Contains);
        // 文字を戻して空になったレイヤは消す
        foreach (var l in doc.Layers.Where(l => l.Name is NameLayer or ElevationLayer && doc.Entities.All(e => e.Layer != l) && l != doc.CurrentLayer).ToList())
            doc.Layers.Remove(l);
        doc.NotifyChanged();
        return restored.Count;
    }
}
