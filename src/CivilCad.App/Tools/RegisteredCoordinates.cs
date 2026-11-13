using System.Globalization;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 「登録した座標」に属する図形の見分け方。登録した座標 = 点名のある点 (PointEntity) と、その点のマーク・点名の文字。
/// これらは測量座標の位置に置く物で、図面を動かす・縮尺を変える操作 (座標設定・縮尺の変更) では図面と一緒に動かさない。
/// 見分け方:
///  - 点名のある PointEntity
///  - Tag が "PTMARK:点名" (座標の描画 CD) または "POINT:点名" (座標読込 PI) の図形 (マーク・点名・標高の文字)
///  - Tag の無い文字 (古い座標読込の点名・標高): 内容が点名 (標高なら F3 の値) と同じで、その点 (またはその点のマーク) から
///    用紙上 <see cref="TextReachMm"/> mm 以内にある物 (座標読込・座標の描画が点名を置く位置は、マークの右上の 1〜5 mm)
/// </summary>
public static class RegisteredCoordinates
{
    /// <summary>座標読込 (PI) が付けるタグの前置き (円・点名・標高の文字)。</summary>
    public const string ImportTagPrefix = "POINT:";

    /// <summary>Tag の無い点名・標高の文字を、点から見て「その点の文字」と見なす範囲 (用紙上の mm)。</summary>
    public const double TextReachMm = 12;

    /// <summary>タグ ("PTMARK:名前" / "POINT:名前") の点名。どちらでもなければ null。</summary>
    public static string? NameOfTag(string? tag)
    {
        if (tag == null) return null;
        if (tag.StartsWith(CoordinateMarks.TagPrefix, StringComparison.Ordinal)) return tag[CoordinateMarks.TagPrefix.Length..];
        if (tag.StartsWith(ImportTagPrefix, StringComparison.Ordinal)) return tag[ImportTagPrefix.Length..];
        return null;
    }

    /// <summary>登録した座標の点か (点名のある点)。</summary>
    public static bool IsPoint(Entity e) => e is PointEntity { Name.Length: > 0 };

    /// <summary>
    /// 登録した座標に属する図形と、その基準の位置 (その点の位置。点が無いマークは図形自身の中心)。
    /// 点自身は自分の位置。図形が無ければ空。
    /// </summary>
    public static Dictionary<Entity, Vec2> Anchors(CadDocument doc)
    {
        var result = new Dictionary<Entity, Vec2>();
        var points = new Dictionary<string, PointEntity>();
        foreach (var e in doc.Entities)
            if (e is PointEntity { Name.Length: > 0 } p) points.TryAdd(p.Name, p);
        if (points.Count == 0)
        {
            foreach (var e in doc.Entities)
                if (NameOfTag(e.Tag) != null) result[e] = e.Bounds.Center;
            return result;
        }

        var markCenters = new Dictionary<string, List<Vec2>>();
        foreach (var e in doc.Entities)
        {
            if (e is PointEntity { Name.Length: > 0 } self) { result[e] = self.Position; continue; }
            if (NameOfTag(e.Tag) is not { } name) continue;
            result[e] = points.TryGetValue(name, out var p) ? p.Position : e.Bounds.Center;
            if (e is not TextEntity)
            {
                if (!markCenters.TryGetValue(name, out var list)) markCenters[name] = list = [];
                list.Add(e.Bounds.Center);
            }
        }

        // Tag の無い点名・標高の文字
        var reach = doc.PaperMmToMeters(TextReachMm);
        var byText = new Dictionary<string, List<PointEntity>>();
        foreach (var p in points.Values)
        {
            if (!byText.TryGetValue(p.Name, out var l)) byText[p.Name] = l = [];
            l.Add(p);
            if (p.Z is double z)
            {
                var zt = z.ToString("F3", CultureInfo.InvariantCulture);
                if (!byText.TryGetValue(zt, out l)) byText[zt] = l = [];
                l.Add(p);
            }
        }
        foreach (var e in doc.Entities)
        {
            if (e is not TextEntity t || t.Tag != null || result.ContainsKey(e) || !byText.TryGetValue(t.Text, out var owners)) continue;
            PointEntity? best = null;
            var bestDist = double.MaxValue;
            foreach (var p in owners)
            {
                var d = t.Position.DistanceTo(p.Position);
                if (markCenters.TryGetValue(p.Name, out var centers))
                    foreach (var c in centers) d = Math.Min(d, t.Position.DistanceTo(c));
                if (d <= reach && d < bestDist) (best, bestDist) = (p, d);
            }
            if (best != null) result[e] = best.Position;
        }
        return result;
    }
}
