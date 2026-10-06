using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

/// <param name="Removed">連結で消える元の図形。</param>
/// <param name="Added">連結してできた図形。</param>
public sealed record JoinResult(IReadOnlyList<Entity> Removed, IReadOnlyList<Entity> Added);

public static class Join
{
    /// <summary>
    /// 分かれている図形をつなぎ直す。
    /// 1. 同じ直線上にある線は 1 本の線にまとめる (間が空いていても埋める)
    /// 2. 同じ円上にある円弧は 1 つの円弧にまとめる (一周すれば円)
    /// 3. 端点が重なる線・開いたポリラインはポリラインにつなぐ (始点に戻れば閉じる)
    /// 円・閉じたポリライン・文字などはそのまま。円弧と線はつながない (ポリラインが円弧を持てないため)。
    /// できた図形は、元の図形のうち先頭のもの (entities の順) のレイヤ・色・線幅を引き継ぐ。
    /// </summary>
    /// <param name="chain">false なら手順 3 (ポリラインにつなぐ) をしない (つなぐのは画面側の結合に任せるとき)。</param>
    public static JoinResult Run(IReadOnlyList<Entity> entities, double tolerance = EditUtil.PointTolerance, bool chain = true)
    {
        var removed = new List<Entity>();
        var added = new List<Entity>();
        var chainable = new List<Entity>(); // 手順 1 の結果も手順 3 の材料にする

        var lines = entities.OfType<LineEntity>().Where(l => l.Length > tolerance).ToList();
        foreach (var group in Groups(lines, (a, b) => Collinear(a, b, tolerance)))
        {
            if (group.Count == 1)
            {
                chainable.Add(group[0]);
                continue;
            }
            var merged = MergeLines(group);
            removed.AddRange(group);
            added.Add(merged);
            chainable.Add(merged);
        }

        var arcs = entities.OfType<ArcEntity>().ToList();
        foreach (var group in Groups(arcs, (a, b) => a.Center.DistanceTo(b.Center) <= tolerance && Math.Abs(a.Radius - b.Radius) <= tolerance))
        {
            if (group.Count == 1) continue;
            removed.AddRange(group);
            added.Add(MergeArcs(group));
        }

        if (!chain) return new JoinResult(removed, added);
        chainable.AddRange(entities.OfType<PolylineEntity>().Where(p => !p.IsClosed && p.Points.Count >= 2));
        foreach (var members in Chains(chainable, tolerance))
        {
            if (members.Count == 1) continue;
            foreach (var e in members)
                if (!added.Remove(e)) removed.Add(e); // 手順 1 で作ったばかりの線は、追加をやめれば済む
            added.Add(BuildPolyline(members, tolerance));
        }
        return new JoinResult(removed, added);
    }

    private static bool Collinear(LineEntity a, LineEntity b, double tol)
    {
        var dir = (a.End - a.Start).Normalized();
        double Dist(Vec2 p) => Math.Abs(dir.Cross(p - a.Start));
        return Dist(b.Start) <= tol && Dist(b.End) <= tol;
    }

    private static LineEntity MergeLines(List<LineEntity> group)
    {
        var first = group[0];
        var origin = first.Start;
        var dir = (first.End - first.Start).Normalized();
        var ts = group.SelectMany(l => new[] { (l.Start - origin).Dot(dir), (l.End - origin).Dot(dir) }).ToList();
        return EditUtil.CopyStyle(first, new LineEntity(first.Layer, origin + dir * ts.Min(), origin + dir * ts.Max()));
    }

    /// <summary>円周上の範囲を重ね合わせ、一番大きい隙間以外を埋める。隙間が無ければ円。</summary>
    private static Entity MergeArcs(List<ArcEntity> group)
    {
        var first = group[0];
        var spans = new List<(double S, double E)>();
        foreach (var arc in group)
        {
            var s = GeoMath.NormalizeAngle(arc.StartAngle);
            var e = s + arc.Sweep;
            spans.Add((s, e));
            if (e > GeoMath.TwoPi) spans.Add((s - GeoMath.TwoPi, e - GeoMath.TwoPi)); // 0° をまたぐ分
        }
        spans.Sort();
        var union = new List<(double S, double E)>();
        foreach (var span in spans)
        {
            if (union.Count > 0 && span.S <= union[^1].E + 1e-9) union[^1] = (union[^1].S, Math.Max(union[^1].E, span.E));
            else union.Add(span);
        }
        var inRange = union.Where(u => u.E > 0 && u.S < GeoMath.TwoPi).ToList();
        var (gapSize, gapStart, gapEnd) = (0.0, 0.0, 0.0);
        for (var i = 0; i < inRange.Count; i++)
        {
            var end = inRange[i].E;
            var nextStart = i + 1 < inRange.Count ? inRange[i + 1].S : inRange[0].S + GeoMath.TwoPi;
            if (nextStart - end > gapSize) (gapSize, gapStart, gapEnd) = (nextStart - end, end, nextStart);
        }
        if (gapSize <= 1e-9) return EditUtil.CopyStyle(first, new CircleEntity(first.Layer, first.Center, first.Radius));
        return EditUtil.CopyStyle(first, new ArcEntity(first.Layer, first.Center, first.Radius, gapEnd, gapStart + GeoMath.TwoPi));
    }

    /// <summary>related でつながるもの同士をまとめる (入力順を保つ)。</summary>
    private static List<List<T>> Groups<T>(List<T> items, Func<T, T, bool> related)
    {
        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
                if (related(items[i], items[j])) parent[Find(j)] = Find(i);
        return [.. Enumerable.Range(0, items.Count).GroupBy(Find).Select(g => g.Select(i => items[i]).ToList())];
    }

    private static List<Vec2> PointsOf(Entity e) => e is LineEntity line ? [line.Start, line.End] : [.. ((PolylineEntity)e).Points];

    /// <summary>端点が重なるものを、つながる順に並べた列にまとめる。</summary>
    private static List<List<Entity>> Chains(List<Entity> items, double tol)
    {
        var result = new List<List<Entity>>();
        var used = new HashSet<Entity>();
        foreach (var seed in items)
        {
            if (!used.Add(seed)) continue;
            var chain = new LinkedList<Entity>([seed]);
            var seedPts = PointsOf(seed);
            var (head, tail) = (seedPts[0], seedPts[^1]);
            var grown = true;
            while (grown && head.DistanceTo(tail) > tol)
            {
                grown = false;
                foreach (var e in items)
                {
                    if (used.Contains(e)) continue;
                    var pts = PointsOf(e);
                    if (pts[0].DistanceTo(tail) <= tol || pts[^1].DistanceTo(tail) <= tol)
                    {
                        tail = pts[0].DistanceTo(tail) <= tol ? pts[^1] : pts[0];
                        chain.AddLast(e);
                    }
                    else if (pts[0].DistanceTo(head) <= tol || pts[^1].DistanceTo(head) <= tol)
                    {
                        head = pts[^1].DistanceTo(head) <= tol ? pts[0] : pts[^1];
                        chain.AddFirst(e);
                    }
                    else continue;
                    used.Add(e);
                    grown = true;
                    if (head.DistanceTo(tail) <= tol) break;
                }
            }
            result.Add([.. chain]);
        }
        return result;
    }

    private static PolylineEntity BuildPolyline(List<Entity> chain, double tol)
    {
        var points = new List<Vec2>();
        foreach (var e in chain)
        {
            var pts = PointsOf(e);
            if (points.Count == 0)
            {
                // 先頭は 2 本目につながる側を終点にする
                var next = PointsOf(chain[1]);
                if (pts[0].DistanceTo(next[0]) <= tol || pts[0].DistanceTo(next[^1]) <= tol) pts.Reverse();
                points.AddRange(pts);
                continue;
            }
            if (pts[^1].DistanceTo(points[^1]) <= tol) pts.Reverse();
            points.AddRange(pts.Skip(1));
        }
        var closed = points.Count > 3 && points[0].DistanceTo(points[^1]) <= tol;
        if (closed) points.RemoveAt(points.Count - 1);
        var first = chain[0];
        return EditUtil.CopyStyle(first, new PolylineEntity(first.Layer, points, closed));
    }
}
