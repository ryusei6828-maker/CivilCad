using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 図形のコピー・貼り付け (Ctrl+C / Ctrl+X / Ctrl+V)。アプリ全体で 1 つなので、あるシートでコピーした図形を別のシートに貼り付けられる。
/// 貼り付けると、図形は同じ名前のレイヤ (無ければ同じ名前・色で作る) に入る。
/// 測量座標の向き (座標設定 CS) がシートで違うときは、測量座標が同じになる位置に置く。
/// プリセット (方位記号など) は用紙上の大きさのまま。
/// </summary>
public static class EntityClipboard
{
    private static List<Entity> _items = [];
    private static double _surveyRotation;
    private static Vec2 _surveyPivot;

    public static bool HasContent => _items.Count > 0;
    public static int Count => _items.Count;

    /// <summary>コピーした図形全体の範囲 (コピー元の座標)。</summary>
    public static Box2 Bounds => _items.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));

    /// <summary>図形をコピーする (後でコピー元を変えても、コピーした時点の形を貼り付ける)。</summary>
    public static void Copy(CadDocument doc, IEnumerable<Entity> entities)
    {
        _items = [.. entities.Select(e => e.Clone())];
        _surveyRotation = doc.SurveyRotation;
        _surveyPivot = doc.SurveyPivot;
    }

    /// <summary>
    /// 貼り付ける図形 (target 用の新しい図形。まだ図面には入れない)。
    /// レイヤは target の同じ名前のレイヤにし、無ければ作る。
    /// </summary>
    public static List<Entity> CreateFor(CadDocument target) => Convert(_items, _surveyRotation, _surveyPivot, target);

    /// <summary>
    /// 別のシートへ複写する図形 (target 用の新しい図形。まだ図面には入れない)。貼り付けと同じ変換 (レイヤ・測量座標の向き・記号の大きさ・新しいグループ)。
    /// </summary>
    public static List<Entity> Transfer(CadDocument source, IEnumerable<Entity> items, CadDocument target)
        => Convert(items, source.SurveyRotation, source.SurveyPivot, target);

    /// <summary>source の図面座標の点を、測量座標が同じになる target の図面座標へ (向きが同じならそのまま)。</summary>
    public static Vec2 MapPoint(CadDocument source, Vec2 p, CadDocument target)
        => source.SurveyRotation == target.SurveyRotation && source.SurveyPivot == target.SurveyPivot
            ? p
            : target.FromSurveyCad(source.ToSurveyCad(p));

    private static List<Entity> Convert(IEnumerable<Entity> items, double surveyRotation, Vec2 surveyPivot, CadDocument target)
    {
        var result = new List<Entity>();
        foreach (var src in items)
        {
            var e = src.Clone();
            e.Layer = target.FindLayer(src.Layer.Name) ?? target.GetOrCreateLayer(src.Layer.Name, src.Layer.Color);
            // コピー元の測量座標 → 貼り付け先の図面座標 (向きが同じなら何もしない)
            if (surveyRotation != target.SurveyRotation || surveyPivot != target.SurveyPivot)
            {
                if (surveyRotation != 0) e.Rotate(surveyPivot, surveyRotation);
                if (target.SurveyRotation != 0) e.Rotate(target.SurveyPivot, -target.SurveyRotation);
            }
            if (e is SymbolEntity s) s.Size = target.PaperMmToMeters(s.SizeMm);
            result.Add(e);
        }
        return Grouping.RenewGroups(result);
    }

    /// <summary>テスト用: 中身を空にする。</summary>
    public static void Clear() => _items = [];
}
