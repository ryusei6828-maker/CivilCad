using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

/// <summary>
/// フィレット (F): 2 本の線の角を、半径 R の円弧でつなぐ。R = 0 なら角まで伸ばす / 切って突き合わせる (コーナー)。
/// どちらの側を残すかは、線をクリックした位置で決める (交点から見てクリックした側を残す)。
/// </summary>
public static class Fillet
{
    /// <param name="Line1">1 本目の線の新しい形。長さ 0 になったら null (消す)。</param>
    /// <param name="Line2">2 本目の線の新しい形。長さ 0 になったら null (消す)。</param>
    /// <param name="Arc">つなぐ円弧。R = 0 なら null。</param>
    public sealed record Result(LineEntity? Line1, LineEntity? Line2, ArcEntity? Arc);

    /// <summary>失敗の理由。</summary>
    public enum Error { None, Parallel, SameLine, TooLarge }

    private const double Eps = 1e-9;

    /// <summary>
    /// 線 l1 (クリック点 pick1) と線 l2 (pick2) をフィレットする。元の線は変えず、新しい図形を返す。
    /// 失敗したら null と理由を返す。
    /// </summary>
    public static Result? Create(LineEntity l1, Vec2 pick1, LineEntity l2, Vec2 pick2, double radius, out Error error)
    {
        error = Error.None;
        if (ReferenceEquals(l1, l2)) { error = Error.SameLine; return null; }
        var d1 = l1.End - l1.Start;
        var d2 = l2.End - l2.Start;
        if (d1.Length < Eps || d2.Length < Eps) { error = Error.SameLine; return null; }
        var cross = d1.Cross(d2);
        if (Math.Abs(cross) < 1e-12 * d1.Length * d2.Length) { error = Error.Parallel; return null; }

        // 交点 x (延長した直線どうし)
        var t = (l2.Start - l1.Start).Cross(d2) / cross;
        var x = l1.Start + d1 * t;

        // 交点から残す側への向き u と、その側で一番遠い端 (残す線の端)
        if (!Side(l1, pick1, x, out var u1, out var far1, out var reach1) ||
            !Side(l2, pick2, x, out var u2, out var far2, out var reach2))
        {
            error = Error.SameLine;
            return null;
        }

        if (radius <= 0)
            return new Result(Make(l1, far1, x), Make(l2, far2, x), null);

        // 2 本の残す向きのなす角 θ、接点までの距離 = R / tan(θ/2)
        var cos = Math.Clamp(u1.Dot(u2), -1, 1);
        var half = Math.Acos(cos) / 2;
        if (half < 1e-9 || Math.PI / 2 - half < 1e-9) { error = Error.Parallel; return null; }
        var tangent = radius / Math.Tan(half);
        if (tangent > reach1 + Eps || tangent > reach2 + Eps) { error = Error.TooLarge; return null; }

        var t1 = x + u1 * tangent;
        var t2 = x + u2 * tangent;
        var center = x + (u1 + u2).Normalized() * (radius / Math.Sin(half));
        var a1 = (t1 - center).Angle;
        var a2 = (t2 - center).Angle;
        // 短い側 (π − θ) を反時計回りで
        var arc = GeoMath.SweepAngle(a1, a2) <= Math.PI
            ? new ArcEntity(l1.Layer, center, radius, a1, a2)
            : new ArcEntity(l1.Layer, center, radius, a2, a1);
        arc.Color = l1.Color;
        arc.LineWeight = l1.LineWeight;
        arc.LineType = l1.LineType;
        return new Result(Make(l1, far1, t1), Make(l2, far2, t2), arc);
    }

    /// <summary>
    /// 交点 x から見てクリック点 pick の側を向く単位ベクトル u と、その側で一番遠い線の端 far、x から far までの距離 reach。
    /// 線の両端とも反対側にある (クリック点が交点の向こう) ときは、伸ばす向きとして近い方の端を使う。
    /// </summary>
    private static bool Side(LineEntity l, Vec2 pick, Vec2 x, out Vec2 u, out Vec2 far, out double reach)
    {
        var dir = (l.End - l.Start).Normalized();
        var s = (pick - x).Dot(dir);
        var ss = (l.Start - x).Dot(dir);
        var se = (l.End - x).Dot(dir);
        // クリックが交点ちょうどなら、長く残る方
        if (Math.Abs(s) < Eps) s = Math.Abs(ss) >= Math.Abs(se) ? ss : se;
        u = s >= 0 ? dir : -dir;
        var (ps, pe) = s >= 0 ? (ss, se) : (-ss, -se);
        far = ps >= pe ? l.Start : l.End;
        reach = Math.Max(ps, pe);
        if (reach < Eps)
        {
            // 線が丸ごと交点の反対側: 交点まで伸ばすだけになるので、そちら向きに直す
            u = -u;
            far = ps <= pe ? l.Start : l.End;
            reach = Math.Max(-ps, -pe);
        }
        return reach >= Eps;
    }

    /// <summary>元の線の属性のまま、far → to の線を作る (向きは元の線に合わせる)。長さ 0 なら null。</summary>
    private static LineEntity? Make(LineEntity original, Vec2 far, Vec2 to)
    {
        if (far.DistanceTo(to) < Eps) return null;
        var line = (LineEntity)original.Clone();
        var keepStart = far == original.Start;
        line.Start = keepStart ? far : to;
        line.End = keepStart ? to : far;
        return line;
    }
}
