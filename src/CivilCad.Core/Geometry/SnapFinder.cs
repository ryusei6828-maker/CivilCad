using CivilCad.Core.Model;

namespace CivilCad.Core.Geometry;

/// <summary>カーソル付近のスナップ点を探す。</summary>
public static class SnapFinder
{
    /// <summary>1 回の探索で調べる曲線の上限 (カーソルに近い順。交点は組み合わせなので多すぎると重くなる)。</summary>
    private const int MaxCurves = 120;

    public static IReadOnlySet<SnapKind> DefaultKinds { get; } = new HashSet<SnapKind>
    {
        SnapKind.Endpoint, SnapKind.Midpoint, SnapKind.Center, SnapKind.Quadrant, SnapKind.Node,
        SnapKind.Intersection, SnapKind.Perpendicular, SnapKind.Tangent,
    };

    /// <summary>
    /// raw から tol 以内で最も近いスナップ点。近接点 (Nearest) は他に何も無いときだけ使う。
    /// basePoint は垂線・接点の基準 (直前に指定した点)。
    /// </summary>
    public static SnapPoint? Find(IEnumerable<Entity> entities, Vec2 raw, double tol, Vec2? basePoint, IReadOnlySet<SnapKind> kinds) =>
        FindAmong(entities.Where(e => e.Bounds.Inflate(tol).Contains(raw)).ToList(), raw, tol, basePoint, kinds);

    /// <summary>候補の図形 (カーソル付近に絞り込み済み) の中から探す。</summary>
    public static SnapPoint? FindAmong(IReadOnlyList<Entity> near, Vec2 raw, double tol, Vec2? basePoint, IReadOnlySet<SnapKind> kinds)
    {
        SnapPoint? best = null;
        var bestD = tol;
        void Consider(Vec2 p, SnapKind kind)
        {
            var d = p.DistanceTo(raw);
            if (d <= bestD) (best, bestD) = (new SnapPoint(p, kind), d);
        }

        // 端点・中点・中心など、図形が持っている点
        foreach (var e in near)
            foreach (var sp in e.SnapPoints)
                if (kinds.Contains(sp.Kind)) Consider(sp.Point, sp.Kind);

        var needCurves = kinds.Contains(SnapKind.Intersection) || kinds.Contains(SnapKind.Nearest) ||
                         basePoint != null && (kinds.Contains(SnapKind.Perpendicular) || kinds.Contains(SnapKind.Tangent));
        if (!needCurves) return best;

        // カーソルの近くを通る線分・円・円弧
        var curves = near.SelectMany(e => e.Curves)
            .Select(c => (Curve: c, D: SnapGeometry.Nearest(c, raw).DistanceTo(raw)))
            .Where(x => x.D <= tol)
            .OrderBy(x => x.D)
            .Take(MaxCurves)
            .Select(x => x.Curve)
            .ToList();

        if (kinds.Contains(SnapKind.Intersection))
            for (var i = 0; i < curves.Count; i++)
                for (var j = i + 1; j < curves.Count; j++)
                    foreach (var p in SnapGeometry.Intersections(curves[i], curves[j]))
                        Consider(p, SnapKind.Intersection);

        if (basePoint is Vec2 b)
        {
            foreach (var c in curves)
            {
                if (kinds.Contains(SnapKind.Perpendicular))
                    foreach (var p in SnapGeometry.PerpendicularFeet(c, b)) Consider(p, SnapKind.Perpendicular);
                if (kinds.Contains(SnapKind.Tangent))
                    foreach (var p in SnapGeometry.TangentPoints(c, b)) Consider(p, SnapKind.Tangent);
            }
        }

        if (best == null && kinds.Contains(SnapKind.Nearest))
            foreach (var c in curves) Consider(SnapGeometry.Nearest(c, raw), SnapKind.Nearest);

        return best;
    }

    public static string Label(SnapKind kind) => kind switch
    {
        SnapKind.Endpoint => "端点",
        SnapKind.Midpoint => "中点",
        SnapKind.Center => "中心",
        SnapKind.Quadrant => "四半円点",
        SnapKind.Node => "挿入点",
        SnapKind.Intersection => "交点",
        SnapKind.Perpendicular => "垂線の足",
        SnapKind.Tangent => "接点",
        SnapKind.Nearest => "近接点",
        _ => kind.ToString(),
    };
}
