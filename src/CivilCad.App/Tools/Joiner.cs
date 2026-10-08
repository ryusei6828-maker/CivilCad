using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 結合: 端点どうしがつながっている線・開いたポリライン・円弧を、1 本のポリラインにまとめる。
/// 一周して始点に戻れば閉じたポリラインにする (塗りつぶし・ハッチングの範囲に使える)。
/// 円弧は細かい折れ線に置き換える。
/// </summary>
public static class Joiner
{
    public sealed record Result(List<PolylineEntity> Joined, List<Entity> Consumed)
    {
        public int ClosedCount => Joined.Count(p => p.IsClosed);
    }

    /// <param name="tolerance">端点を同じとみなす距離 (m)。</param>
    public static Result Join(IEnumerable<Entity> entities, double tolerance)
    {
        var pieces = entities.Select(e => (Entity: e, Points: PiecePoints(e))).Where(x => x.Points is { Count: >= 2 })
            .Select(x => (x.Entity, Points: x.Points!)).ToList();
        var used = new bool[pieces.Count];
        var joined = new List<PolylineEntity>();
        var consumed = new List<Entity>();

        for (var i = 0; i < pieces.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var chain = new List<Vec2>(pieces[i].Points);
            var members = new List<Entity> { pieces[i].Entity };

            // 終点側、始点側の順に、つながる部品を足していく
            foreach (var atEnd in new[] { true, false })
            {
                while (!IsClosed(chain, tolerance))
                {
                    var tip = atEnd ? chain[^1] : chain[0];
                    // いちばん近い端点を持つ未使用の部品 (許容差以内)
                    var best = -1;
                    var bestD = tolerance * (1 + 1e-9) + 1e-12;
                    var matchedStart = false;
                    for (var j = 0; j < pieces.Count; j++)
                    {
                        if (used[j]) continue;
                        var p = pieces[j].Points;
                        var dStart = tip.DistanceTo(p[0]);
                        if (dStart < bestD) (best, bestD, matchedStart) = (j, dStart, true);
                        var dEnd = tip.DistanceTo(p[^1]);
                        if (dEnd < bestD) (best, bestD, matchedStart) = (j, dEnd, false);
                    }
                    if (best < 0) break;
                    // 後ろに足すときは部品の始点が先端に、前に足すときは部品の終点が先端に来る向きにする
                    var reverse = atEnd ? !matchedStart : matchedStart;
                    used[best] = true;
                    members.Add(pieces[best].Entity);
                    var add = reverse ? Enumerable.Reverse(pieces[best].Points).ToList() : pieces[best].Points;
                    if (atEnd) chain.AddRange(add.Skip(1));
                    else chain.InsertRange(0, add.Take(add.Count - 1));
                }
            }

            if (members.Count < 2) continue; // 1 本だけなら何もしない
            var closed = IsClosed(chain, tolerance) && chain.Count >= 4;
            if (closed) chain.RemoveAt(chain.Count - 1);
            var first = members[0];
            var pl = new PolylineEntity(first.Layer, chain, closed)
            {
                Color = first.Color,
                LineWeight = first.LineWeight,
                LineType = first.LineType,
                Tag = first.Tag,
            };
            joined.Add(pl);
            consumed.AddRange(members);
        }
        return new Result(joined, consumed);
    }

    private static bool IsClosed(List<Vec2> chain, double tol) => chain.Count >= 3 && chain[0].DistanceTo(chain[^1]) <= tol;

    /// <summary>結合できる図形の点列 (始点 → 終点)。閉じた図形や結合できない図形は null。</summary>
    private static List<Vec2>? PiecePoints(Entity e) => e switch
    {
        LineEntity l => [l.Start, l.End],
        PolylineEntity { IsClosed: false, Filled: false } p when p.Points.Count >= 2 => [.. p.Points],
        ArcEntity a => ArcPoints(a),
        _ => null,
    };

    private static List<Vec2> ArcPoints(ArcEntity a)
    {
        var n = Math.Max(4, (int)Math.Ceiling(Math.Abs(a.Sweep) / (Math.PI / 72))); // 2.5° ごと
        return [.. Enumerable.Range(0, n + 1).Select(k => a.Center + Vec2.FromAngle(a.StartAngle + a.Sweep * k / n) * a.Radius)];
    }
}
