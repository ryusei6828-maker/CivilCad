using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>図形を、囲まれた部分 (RegionFinder) の境界にする変換。面積・ハッチング・塗りつぶしで共有する。</summary>
public static class BoundaryCurves
{
    /// <summary>図形を境界の曲線にする。線・ポリライン・円弧・円だけが対象で、ハッチ・文字・寸法などは空。閉じ方は HatchTool.ClosedLoop と同じ。</summary>
    public static IEnumerable<BoundaryCurve> FromEntity(Entity e)
    {
        switch (e)
        {
            case LineEntity l:
                yield return BoundaryCurve.Segment(l.Start, l.End);
                break;
            case PolylineEntity pl:
                var pts = pl.Points;
                for (var i = 0; i + 1 < pts.Count; i++) yield return BoundaryCurve.Segment(pts[i], pts[i + 1]);
                if ((pl.IsClosed || pl.Filled) && pts.Count > 2) yield return BoundaryCurve.Segment(pts[^1], pts[0]);
                break;
            case ArcEntity a:
                yield return BoundaryCurve.Arc(a.Center, a.Radius, a.StartAngle, a.Sweep);
                break;
            case CircleEntity c:
                yield return BoundaryCurve.Circle(c.Center, c.Radius);
                break;
        }
    }
}

/// <summary>
/// 表示中の図形から作った「囲まれた部分」の地図を使い回す。図面 (<see cref="CadDocument.Revision"/>・図形の数・レイヤの表示状態) が
/// 変わったときだけ作り直すので、マウス移動のたびに調べても軽い。
/// </summary>
public sealed class VisibleRegionCache
{
    private CadDocument? _doc;
    private (int Revision, int Count, int Visibility) _key;
    private RegionMap _map = RegionMap.Empty;

    /// <summary>図面の表示中の図形すべてを境界にした地図。</summary>
    public RegionMap Get(CadDocument doc)
    {
        var visibility = 0;
        foreach (var l in doc.Layers) visibility = HashCode.Combine(visibility, l.IsVisible);
        var key = (doc.Revision, doc.Entities.Count, visibility);
        if (_doc == doc && _key == key) return _map;
        _map = RegionFinder.Build(doc.Entities.Where(doc.IsVisible).SelectMany(BoundaryCurves.FromEntity));
        _doc = doc;
        _key = key;
        return _map;
    }
}
