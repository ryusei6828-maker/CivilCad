using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

/// <summary>
/// 面取り (CHA): 2 本の線の角を斜めに落とす。クリックした点の側を残す。
/// 届いていなければ伸ばし、はみ出していれば切る。開いたポリラインは端の線分を別の線につなげられ、
/// 同じポリラインの隣り合う線分なら頂点を落とす。
/// </summary>
public static class Chamfer
{
    private const double Eps = 1e-7;

    /// <param name="Replaced">元の図形 → 直した図形。</param>
    /// <param name="Cut">角に足す斜めの線。距離が 0 で角を出すだけのとき・ポリラインの頂点を落としたときは null。</param>
    public sealed record Result(IReadOnlyList<(Entity Old, Entity New)> Replaced, LineEntity? Cut);

    /// <summary>d1・d2 は角から a 側・b 側へ測る距離 (両方 0 なら角を出すだけ)。作れなければ null と理由。</summary>
    public static Result? Create(Entity a, Vec2 pickA, Entity b, Vec2 pickB, double d1, double d2, out string? error)
    {
        error = null;
        if (d1 < 0 || d2 < 0)
        {
            error = "距離は 0 以上にしてください。";
            return null;
        }
        if (a == b) return Vertex(a, pickA, pickB, d1, d2, out error);
        if (Edge.Of(a, pickA, out error) is not { } ea || Edge.Of(b, pickB, out error) is not { } eb) return null;
        if (CurveIntersect.LineLine(ea.A, ea.B, eb.A, eb.B) is not var (t, _))
        {
            error = "2 つの線が交わりません (平行です)。";
            return null;
        }
        var corner = ea.A + (ea.B - ea.A) * t;
        var keepA = ea.KeepDirection(corner);
        var keepB = eb.KeepDirection(corner);
        var ta = corner + keepA * d1;
        var tb = corner + keepB * d2;
        if (ea.Trimmed(ta, keepA) is not { } newA || eb.Trimmed(tb, keepB) is not { } newB)
        {
            error = "距離が大きすぎます (線より長くなります)。";
            return null;
        }
        LineEntity? cut = null;
        if (ta.DistanceTo(tb) > Eps)
        {
            cut = EditUtil.CopyStyle(a, new LineEntity(a.Layer, ta, tb));
            if (a.GroupId == b.GroupId) cut.GroupId = a.GroupId;
        }
        return new Result([(a, newA), (b, newB)], cut);
    }

    /// <summary>同じポリラインの隣り合う線分で挟まれた頂点を落とす。</summary>
    private static Result? Vertex(Entity entity, Vec2 pickA, Vec2 pickB, double d1, double d2, out string? error)
    {
        error = null;
        if (entity is not PolylineEntity pl)
        {
            error = "別の線をクリックしてください (同じ図形で選べるのはポリラインの隣り合う線分だけです)。";
            return null;
        }
        if (d1 == 0 && d2 == 0)
        {
            error = "同じポリラインの角には、0 より大きい距離が必要です。";
            return null;
        }
        var ka = NearestSegment(pl, pickA);
        var kb = NearestSegment(pl, pickB);
        var count = pl.Points.Count;
        var segments = pl.IsClosed ? count : count - 1;
        int vertex;
        if (ka != kb && (ka + 1) % segments == kb && (pl.IsClosed || kb > ka)) vertex = kb;
        else if (ka != kb && (kb + 1) % segments == ka && (pl.IsClosed || ka > kb)) vertex = ka;
        else
        {
            error = "同じポリラインでは、隣り合う線分をクリックしてください。";
            return null;
        }

        var v = pl.Points[vertex];
        var prev = pl.Points[(vertex - 1 + count) % count];
        var next = pl.Points[(vertex + 1) % count];
        var up = (prev - v).Normalized();
        var un = (next - v).Normalized();
        if (Math.Abs(up.Cross(un)) < 1e-9)
        {
            error = "この頂点は曲がっていません。";
            return null;
        }
        // 線分 k は点 k から始まるので、頂点で始まらない方が手前の線分
        var (dp, dn) = ka != vertex ? (d1, d2) : (d2, d1);
        if (dp > prev.DistanceTo(v) + Eps || dn > next.DistanceTo(v) + Eps)
        {
            error = "距離が大きすぎます (線分より長くなります)。";
            return null;
        }

        var result = (PolylineEntity)pl.Clone();
        result.Points.RemoveAt(vertex);
        result.Points.InsertRange(vertex, [v + up * dp, v + un * dn]);
        // 線分の長さいっぱいまで落として重なった点を除く
        for (var i = result.Points.Count - 1; i > 0; i--)
            if (result.Points[i].DistanceTo(result.Points[i - 1]) <= Eps) result.Points.RemoveAt(i);
        if (result.IsClosed && result.Points.Count > 1 && result.Points[^1].DistanceTo(result.Points[0]) <= Eps) result.Points.RemoveAt(result.Points.Count - 1);
        return new Result([(pl, result)], null);
    }

    private static int NearestSegment(PolylineEntity pl, Vec2 p)
    {
        var best = 0;
        var bestD = double.PositiveInfinity;
        var k = 0;
        foreach (var (a, b) in pl.Segments)
        {
            var d = GeoMath.DistanceToSegment(p, a, b);
            if (d < bestD) (best, bestD) = (k, d);
            k++;
        }
        return best;
    }

    /// <summary>角をつくる片方の線 (線分・開いたポリラインの端の線分)。</summary>
    /// <param name="Side">残す側を示す点。</param>
    /// <param name="Ends">残せる端 (ポリラインは途中につながる側だけ)。</param>
    /// <param name="Rebuild">角の点・残す端 から、直した図形を作る。</param>
    private sealed record Edge(Vec2 A, Vec2 B, Vec2 Side, IReadOnlyList<Vec2> Ends, Func<Vec2, Vec2, Entity> Rebuild)
    {
        private Vec2 U => (B - A).Normalized();

        /// <summary>点 t から見て残す側へ向かう単位ベクトル (クリックした側)。</summary>
        public Vec2 KeepDirection(Vec2 t)
        {
            var s = (Side - t).Dot(U);
            if (Math.Abs(s) > Eps) return s > 0 ? U : -U;
            // クリックが角の上: 長く残る方へ
            var far = Ends.MaxBy(e => e.DistanceTo(t));
            return (far - t).Dot(U) >= 0 ? U : -U;
        }

        /// <summary>t から keep の向きの部分だけを残した図形 (t が線の外なら伸ばす)。残る部分が無ければ null。</summary>
        public Entity? Trimmed(Vec2 t, Vec2 keep)
        {
            var end = Ends.MaxBy(e => (e - t).Dot(keep));
            return (end - t).Dot(keep) > Eps ? Rebuild(t, end) : null;
        }

        public static Edge? Of(Entity e, Vec2 pick, out string? error)
        {
            error = null;
            switch (e)
            {
                case LineEntity line when line.Length > Eps:
                    return new Edge(line.Start, line.End, pick, [line.Start, line.End], (t, keep) =>
                    {
                        var r = (LineEntity)line.Clone();
                        if (keep == line.Start) r.End = t;
                        else r.Start = t;
                        return r;
                    });
                case PolylineEntity { IsClosed: true }:
                    error = "閉じたポリラインは、同じポリラインの隣り合う線分を 2 回クリックして頂点を落とします。";
                    return null;
                case PolylineEntity pl when pl.Points.Count >= 2:
                {
                    var k = NearestSegment(pl, pick);
                    var last = pl.Points.Count - 2;
                    if (k != 0 && k != last)
                    {
                        error = "ポリラインは端の線分をクリックしてください (途中の線分は別の線につなげません)。";
                        return null;
                    }
                    var a = pl.Points[k];
                    var b = pl.Points[k + 1];
                    // 2 点だけなら線と同じで、クリックした側を残す。3 点以上は端の点を動かし、途中の点は動かさない
                    if (k == 0 && last == 0)
                        return new Edge(a, b, pick, [a, b], (t, keep) => WithPoint(pl, keep == a ? 1 : 0, t));
                    return k == 0
                        ? new Edge(a, b, b, [b], (t, _) => WithPoint(pl, 0, t))
                        : new Edge(a, b, a, [a], (t, _) => WithPoint(pl, pl.Points.Count - 1, t));
                }
                default:
                    error = "線・ポリラインが対象です (円・円弧は面取りできません)。";
                    return null;
            }
        }

        private static PolylineEntity WithPoint(PolylineEntity pl, int index, Vec2 p)
        {
            var r = (PolylineEntity)pl.Clone();
            r.Points[index] = p;
            return r;
        }
    }
}
