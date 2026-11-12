namespace CivilCad.Core.Geometry;

/// <summary>囲まれた部分を作る境界の曲線 (直線の線分・円弧・円)。</summary>
public sealed class BoundaryCurve
{
    private BoundaryCurve() { }

    public bool IsArc { get; private init; }
    public Vec2 A { get; private init; }
    public Vec2 B { get; private init; }
    public Vec2 Center { get; private init; }
    public double Radius { get; private init; }

    /// <summary>円弧の始点の角度 (rad)。反時計回りに <see cref="Sweep"/> だけ進む。</summary>
    public double Start { get; private init; }
    public double Sweep { get; private init; }

    public static BoundaryCurve Segment(Vec2 a, Vec2 b) => new() { A = a, B = b };

    public static BoundaryCurve Arc(Vec2 center, double radius, double start, double sweep) =>
        new() { IsArc = true, Center = center, Radius = radius, Start = start, Sweep = Math.Min(sweep, GeoMath.TwoPi) };

    public static BoundaryCurve Circle(Vec2 center, double radius) => Arc(center, radius, 0, GeoMath.TwoPi);

    internal bool IsValid => IsArc ? Radius > 0 && Sweep > 0 : A.DistanceTo(B) > 0;
}

/// <summary>囲まれた部分。Loops[0] が外形、残りは中抜き (描画用の点列)。面積・周長は円弧を正確に扱った値。</summary>
public sealed record Region(IReadOnlyList<IReadOnlyList<Vec2>> Loops, double Area, double Perimeter);

/// <summary>
/// 線分・円弧・円が交わってできる閉じた部分を探す (平面の分割)。
/// 曲線を交点で切り、半辺のグラフにして面をたどり、クリック点を含む最小の面を返す。
/// </summary>
public static class RegionFinder
{
    private const double MaxStep = 0.5 * Math.PI / 180;

    private readonly record struct Edge(int U, int V, bool IsArc, double R, double Delta);

    internal sealed record Face(List<Vec2> Points, double Area, double Perimeter, int Comp)
    {
        public Box2 Box { get; } = Box2.FromPoints(Points);
    }

    /// <summary>点 p を含む、境界に囲まれた最小の部分。囲まれていなければ null。</summary>
    public static Region? Find(IEnumerable<BoundaryCurve> input, Vec2 p) => Build(input).Find(p);

    /// <summary>曲線全体から面を一度だけ作る。同じ図面で何度も調べるときは、これを使い回す (<see cref="RegionMap.Find"/>)。</summary>
    public static RegionMap Build(IEnumerable<BoundaryCurve> input)
    {
        var curves = input.Where(c => c.IsValid).ToList();
        if (curves.Count == 0) return RegionMap.Empty;

        // 許容誤差 (図面の大きさに比例)
        var boxes = curves.Select(BoxOf).ToList();
        var box = Box2.Empty;
        foreach (var b in boxes) box = box.Union(b);
        var extent = Math.Max(box.Width, box.Height);
        var tol = Math.Max(1e-9, 1e-7 * extent);

        // 交点で切る位置 (曲線のパラメータ)
        var splits = curves.Select(c => new List<double> { 0, Length(c) }).ToList();
        // MinX 順に並べ、X が重なる範囲だけ調べる (掃引)
        var order = Enumerable.Range(0, curves.Count).OrderBy(i => boxes[i].Min.X).ToArray();
        for (var oi = 0; oi < order.Length; oi++)
        {
            var i = order[oi];
            var bi = Inflate(boxes[i], tol);
            for (var oj = oi + 1; oj < order.Length; oj++)
            {
                var j = order[oj];
                if (boxes[j].Min.X > bi.Max.X) break;
                if (!bi.Intersects(boxes[j])) continue;
                Intersect(curves, splits, Math.Min(i, j), Math.Max(i, j), tol);
            }
        }

        // 辺 (チョード) と頂点
        var verts = new List<Vec2>();
        var grid = new Dictionary<(long, long), List<int>>();
        var cell = tol * 2;
        int Vertex(Vec2 q)
        {
            var cx = (long)Math.Floor(q.X / cell);
            var cy = (long)Math.Floor(q.Y / cell);
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((cx + dx, cy + dy), out var l))
                        foreach (var id in l)
                            if (verts[id].DistanceTo(q) <= tol) return id;
            verts.Add(q);
            if (!grid.TryGetValue((cx, cy), out var own)) grid[(cx, cy)] = own = [];
            own.Add(verts.Count - 1);
            return verts.Count - 1;
        }

        var edges = new List<Edge>();
        var pairs = new Dictionary<(int, int), int>();
        void AddEdge(Vec2 a, Vec2 b, bool arc, double r, double delta)
        {
            var u = Vertex(a);
            var v = Vertex(b);
            if (u == v) return;
            var key = u < v ? (u, v) : (v, u);
            if (pairs.TryGetValue(key, out var existing))
            {
                if (arc && !edges[existing].IsArc) edges[existing] = new Edge(u, v, true, r, delta);
                return;
            }
            pairs[key] = edges.Count;
            edges.Add(new Edge(u, v, arc, r, delta));
        }

        for (var k = 0; k < curves.Count; k++)
        {
            var c = curves[k];
            var len = Length(c);
            var ptol = tol / (c.IsArc ? c.Radius : c.A.DistanceTo(c.B));
            var ts = new List<double> { 0 };
            foreach (var t in splits[k].Where(t => t > ptol && t < len - ptol).Order())
                if (t - ts[^1] > ptol) ts.Add(t);
            ts.Add(len);
            for (var s = 0; s + 1 < ts.Count; s++)
            {
                if (!c.IsArc)
                {
                    AddEdge(PointAt(c, ts[s]), PointAt(c, ts[s + 1]), false, 0, 0);
                    continue;
                }
                var sub = ts[s + 1] - ts[s];
                var n = Math.Max(1, (int)Math.Ceiling(sub / Math.Min(MaxStep, c.Sweep / 4) - 1e-9));
                for (var j = 0; j < n; j++)
                    AddEdge(PointAt(c, ts[s] + sub * j / n), PointAt(c, ts[s] + sub * (j + 1) / n), true, c.Radius, sub / n);
            }
        }

        // 行き止まりの辺を取り除く
        var alive = new bool[edges.Count];
        Array.Fill(alive, true);
        var incident = new List<int>[verts.Count];
        for (var i = 0; i < incident.Length; i++) incident[i] = [];
        for (var e = 0; e < edges.Count; e++)
        {
            incident[edges[e].U].Add(e);
            incident[edges[e].V].Add(e);
        }
        var degree = incident.Select(l => l.Count).ToArray();
        var queue = new Queue<int>(Enumerable.Range(0, verts.Count).Where(v => degree[v] == 1));
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            if (degree[v] != 1) continue;
            foreach (var e in incident[v])
            {
                if (!alive[e]) continue;
                alive[e] = false;
                degree[edges[e].U]--;
                degree[edges[e].V]--;
                var other = edges[e].U == v ? edges[e].V : edges[e].U;
                if (degree[other] == 1) queue.Enqueue(other);
            }
        }
        if (!alive.Any(a => a)) return RegionMap.Empty;

        // 半辺: 2e = U→V、2e+1 = V→U。頂点ごとに角度順 (反時計回り) に並べる
        int Origin(int h) => (h & 1) == 0 ? edges[h >> 1].U : edges[h >> 1].V;
        int Dest(int h) => (h & 1) == 0 ? edges[h >> 1].V : edges[h >> 1].U;
        double Dir(int h) => (verts[Dest(h)] - verts[Origin(h)]).Angle;
        var outgoing = new List<int>[verts.Count];
        for (var i = 0; i < outgoing.Length; i++) outgoing[i] = [];
        for (var e = 0; e < edges.Count; e++)
        {
            if (!alive[e]) continue;
            outgoing[edges[e].U].Add(2 * e);
            outgoing[edges[e].V].Add(2 * e + 1);
        }
        var posInList = new int[edges.Count * 2];
        for (var v = 0; v < outgoing.Length; v++)
        {
            var l = outgoing[v];
            if (l.Count == 0) continue;
            l.Sort((a, b) => Dir(a).CompareTo(Dir(b)));
            for (var i = 0; i < l.Count; i++) posInList[l[i]] = i;
        }
        // 到着した辺の次は、戻る向きの辺から時計回りに最初の辺 (面を左に見て進む)
        int Next(int h)
        {
            var l = outgoing[Dest(h)];
            return l[(posInList[h ^ 1] - 1 + l.Count) % l.Count];
        }

        // 連結成分
        var comp = new int[verts.Count];
        Array.Fill(comp, -1);
        var compCount = 0;
        for (var s = 0; s < verts.Count; s++)
        {
            if (comp[s] >= 0 || outgoing[s].Count == 0) continue;
            var st = new Stack<int>();
            st.Push(s);
            comp[s] = compCount;
            while (st.Count > 0)
            {
                var v = st.Pop();
                foreach (var h in outgoing[v])
                {
                    var w = Dest(h);
                    if (comp[w] >= 0) continue;
                    comp[w] = compCount;
                    st.Push(w);
                }
            }
            compCount++;
        }

        // 面をたどる
        var visited = new bool[edges.Count * 2];
        var faces = new List<Face>();
        for (var e = 0; e < edges.Count; e++)
        {
            if (!alive[e]) continue;
            for (var h0 = 2 * e; h0 <= 2 * e + 1; h0++)
            {
                if (visited[h0]) continue;
                var pts = new List<Vec2>();
                double sum = 0, correction = 0, perimeter = 0;
                var h = h0;
                do
                {
                    visited[h] = true;
                    var a = verts[Origin(h)];
                    var b = verts[Dest(h)];
                    pts.Add(a);
                    sum += a.Cross(b);
                    var ed = edges[h >> 1];
                    if (ed.IsArc)
                    {
                        var seg = ed.R * ed.R / 2 * (ed.Delta - Math.Sin(ed.Delta));
                        correction += (h & 1) == 0 ? seg : -seg; // 円弧を反時計回りに進むなら、弦の外側へふくらむ
                        perimeter += ed.R * ed.Delta;
                    }
                    else perimeter += a.DistanceTo(b);
                    h = Next(h);
                } while (h != h0);
                faces.Add(new Face(pts, sum / 2 + correction, perimeter, comp[Origin(h0)]));
            }
        }

        return new RegionMap(faces, 1e-12 * extent * extent);
    }

    /// <summary>点が多角形の内側か (偶奇規則)。</summary>
    internal static bool Contains(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static Box2 Inflate(Box2 b, double d) => new(b.Min - new Vec2(d, d), b.Max + new Vec2(d, d));

    private static Box2 BoxOf(BoundaryCurve c) => c.IsArc
        ? new Box2(c.Center - new Vec2(c.Radius, c.Radius), c.Center + new Vec2(c.Radius, c.Radius))
        : Box2.FromPoints(c.A, c.B);

    /// <summary>パラメータの長さ。線分は 0〜1、円弧は 0〜掃引角。</summary>
    private static double Length(BoundaryCurve c) => c.IsArc ? c.Sweep : 1;

    private static Vec2 PointAt(BoundaryCurve c, double t) => c.IsArc ? c.Center + Vec2.FromAngle(c.Start + t) * c.Radius : c.A + (c.B - c.A) * t;

    private static bool IsFull(BoundaryCurve c) => c.IsArc && c.Sweep >= GeoMath.TwoPi - 1e-12;

    /// <summary>点が曲線の上 (許容誤差内) ならそのパラメータ。</summary>
    private static double? ParamOn(BoundaryCurve c, Vec2 q, double tol)
    {
        if (!c.IsArc)
        {
            var v = c.B - c.A;
            var t = Math.Clamp((q - c.A).Dot(v) / v.LengthSquared, 0, 1);
            return (c.A + v * t).DistanceTo(q) <= tol ? t : null;
        }
        var d = q - c.Center;
        if (Math.Abs(d.Length - c.Radius) > tol) return null;
        var u = GeoMath.NormalizeAngle(d.Angle - c.Start);
        if (IsFull(c)) return u;
        var eu = tol / c.Radius;
        if (u <= c.Sweep + eu) return Math.Min(u, c.Sweep);
        if (u >= GeoMath.TwoPi - eu) return 0;
        return null;
    }

    private static IEnumerable<Vec2> Ends(BoundaryCurve c)
    {
        if (!c.IsArc)
        {
            yield return c.A;
            yield return c.B;
            yield break;
        }
        yield return PointAt(c, 0);
        if (!IsFull(c)) yield return PointAt(c, c.Sweep);
    }

    private static void Intersect(List<BoundaryCurve> curves, List<List<double>> splits, int i, int j, double tol)
    {
        var a = curves[i];
        var b = curves[j];
        void Both(Vec2 q)
        {
            if (ParamOn(a, q, tol * 2) is double ta) splits[i].Add(ta);
            if (ParamOn(b, q, tol * 2) is double tb) splits[j].Add(tb);
        }

        // 端点がもう一方の上にある (T 字・重なり)
        foreach (var e in Ends(a))
            if (ParamOn(b, e, tol) is double t) splits[j].Add(t);
        foreach (var e in Ends(b))
            if (ParamOn(a, e, tol) is double t) splits[i].Add(t);

        if (!a.IsArc && !b.IsArc)
        {
            var d1 = a.B - a.A;
            var d2 = b.B - b.A;
            var den = d1.Cross(d2);
            if (Math.Abs(den) <= 1e-14 * d1.Length * d2.Length) return;
            var w = b.A - a.A;
            var t = w.Cross(d2) / den;
            var u = w.Cross(d1) / den;
            var et = tol / d1.Length;
            var eu = tol / d2.Length;
            if (t >= -et && t <= 1 + et && u >= -eu && u <= 1 + eu)
            {
                splits[i].Add(Math.Clamp(t, 0, 1));
                splits[j].Add(Math.Clamp(u, 0, 1));
            }
        }
        else if (a.IsArc && b.IsArc)
        {
            var dv = b.Center - a.Center;
            var d = dv.Length;
            if (d < 1e-12) return;
            if (d > a.Radius + b.Radius + tol || d < Math.Abs(a.Radius - b.Radius) - tol) return;
            var x = (a.Radius * a.Radius - b.Radius * b.Radius + d * d) / (2 * d);
            var h = Math.Sqrt(Math.Max(0, a.Radius * a.Radius - x * x));
            var dir = dv / d;
            var mid = a.Center + dir * x;
            Both(mid + dir.LeftNormal * h);
            if (h > tol) Both(mid - dir.LeftNormal * h);
        }
        else
        {
            var segIsA = !a.IsArc;
            var seg = segIsA ? a : b;
            var arc = segIsA ? b : a;
            var v = seg.B - seg.A;
            var f = seg.A - arc.Center;
            var len2 = v.LengthSquared;
            var t0 = -f.Dot(v) / len2;
            var dist = (f + v * t0).Length;              // 中心から直線までの距離
            if (dist > arc.Radius + tol) return;
            var half = Math.Sqrt(Math.Max(0, arc.Radius * arc.Radius - dist * dist)) / Math.Sqrt(len2);
            var ts = Math.Abs(dist - arc.Radius) <= tol ? new[] { t0 } : new[] { t0 - half, t0 + half };
            var et = tol / Math.Sqrt(len2);
            foreach (var t in ts)
            {
                if (t < -et || t > 1 + et) continue;
                var q = seg.A + v * Math.Clamp(t, 0, 1);
                var onCircle = arc.Center + (q - arc.Center).Normalized() * arc.Radius; // 円周上に載せ直す
                if (ParamOn(arc, onCircle, tol * 2) is not double ua) continue;
                splits[segIsA ? i : j].Add(Math.Clamp(t, 0, 1));
                splits[segIsA ? j : i].Add(ua);
            }
        }
    }
}

/// <summary>作り終えた面の集まり。<see cref="Find"/> は軽い (面の外接矩形で絞ってから内外判定)。</summary>
public sealed class RegionMap
{
    public static readonly RegionMap Empty = new([], 0);

    private readonly List<RegionFinder.Face> _inner;   // 面積 > 0 (囲まれた部分)
    private readonly List<RegionFinder.Face> _outer;   // 面積 < 0 (連結成分の外周 = 中抜きの候補)

    internal RegionMap(List<RegionFinder.Face> faces, double minArea)
    {
        _inner = faces.Where(f => f.Area > minArea).ToList();
        _outer = faces.Where(f => f.Area < -minArea).ToList();
        MinArea = minArea;
    }

    private double MinArea { get; }

    /// <summary>囲まれた部分の数。</summary>
    public int FaceCount => _inner.Count;

    /// <summary>点 p を含む最小の部分 (別の連結成分の閉じた図形は中抜き)。囲まれていなければ null。</summary>
    public Region? Find(Vec2 p)
    {
        RegionFinder.Face? best = null;
        foreach (var f in _inner)
        {
            if (best != null && f.Area >= best.Area) continue;
            if (!f.Box.Contains(p) || !RegionFinder.Contains(f.Points, p)) continue;
            best = f;
        }
        if (best == null) return null;

        var holes = _outer.Where(f => f.Comp != best.Comp && best.Box.Contains(f.Box) && RegionFinder.Contains(best.Points, f.Points[0]) && !RegionFinder.Contains(f.Points, p)).ToList();
        holes = holes.Where(h => !holes.Any(o => o != h && Math.Abs(o.Area) > Math.Abs(h.Area) && o.Box.Contains(h.Box) && RegionFinder.Contains(o.Points, h.Points[0]))).ToList();

        var loops = new List<IReadOnlyList<Vec2>> { best.Points };
        loops.AddRange(holes.Select(h => (IReadOnlyList<Vec2>)h.Points));
        return new Region(loops, best.Area + holes.Sum(h => h.Area), best.Perimeter + holes.Sum(h => h.Perimeter));
    }
}
