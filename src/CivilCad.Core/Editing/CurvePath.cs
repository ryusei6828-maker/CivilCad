using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Editing;

/// <summary>
/// 線・円弧・円・ポリラインを「始点からの長さ s」で扱うための共通の形。トリムで図形の一部を切り出すのに使う。
/// 閉じた形状 (円・閉じたポリライン) は s が Length を超えると先頭に回り込む。
/// </summary>
internal abstract class CurvePath
{
    public abstract double Length { get; }
    public abstract bool Closed { get; }
    public abstract IReadOnlyList<Curve> Curves { get; }

    /// <summary>p に一番近い位置の s。</summary>
    public abstract double ParamOf(Vec2 p);

    /// <summary>s0〜s1 の部分 (s0 &lt; s1。閉じた形状は s1 が Length を超えてよい)。</summary>
    public abstract Entity Sub(double s0, double s1);

    public static CurvePath? Of(Entity e) => e switch
    {
        LineEntity line when line.Length > 0 => new LinePath(line),
        ArcEntity arc when arc.Radius > 0 => new ArcPath(arc),
        CircleEntity circle when circle.Radius > 0 => new CirclePath(circle),
        PolylineEntity pl when pl.Points.Count >= 2 && pl.Length > 0 => new PolyPath(pl),
        _ => null,
    };

    private sealed class LinePath(LineEntity line) : CurvePath
    {
        public override double Length => line.Length;
        public override bool Closed => false;
        public override IReadOnlyList<Curve> Curves => [new SegmentCurve(line.Start, line.End)];
        public override double ParamOf(Vec2 p) => GeoMath.ClosestParamOnSegment(p, line.Start, line.End) * Length;
        public override Entity Sub(double s0, double s1) =>
            EditUtil.CopyStyle(line, new LineEntity(line.Layer, PointAt(s0), PointAt(s1)));
        private Vec2 PointAt(double s) => line.Start + (line.End - line.Start) * (s / Length);
    }

    private sealed class ArcPath(ArcEntity arc) : CurvePath
    {
        public override double Length => arc.Length;
        public override bool Closed => false;
        public override IReadOnlyList<Curve> Curves => [.. arc.Curves];

        public override double ParamOf(Vec2 p)
        {
            var offset = GeoMath.NormalizeAngle((p - arc.Center).Angle - arc.StartAngle);
            // 円弧の外側は近い方の端に寄せる
            if (offset > arc.Sweep) offset = offset - arc.Sweep < GeoMath.TwoPi - offset ? arc.Sweep : 0;
            return offset * arc.Radius;
        }

        public override Entity Sub(double s0, double s1) => EditUtil.CopyStyle(arc,
            new ArcEntity(arc.Layer, arc.Center, arc.Radius, arc.StartAngle + s0 / arc.Radius, arc.StartAngle + s1 / arc.Radius));
    }

    private sealed class CirclePath(CircleEntity circle) : CurvePath
    {
        public override double Length => GeoMath.TwoPi * circle.Radius;
        public override bool Closed => true;
        public override IReadOnlyList<Curve> Curves => [.. circle.Curves];
        public override double ParamOf(Vec2 p) => GeoMath.NormalizeAngle((p - circle.Center).Angle) * circle.Radius;
        public override Entity Sub(double s0, double s1) => EditUtil.CopyStyle(circle,
            new ArcEntity(circle.Layer, circle.Center, circle.Radius, s0 / circle.Radius, s1 / circle.Radius));
    }

    private sealed class PolyPath : CurvePath
    {
        private readonly PolylineEntity _pl;
        private readonly List<(Vec2 A, Vec2 B)> _segs;
        private readonly double[] _cum; // 各頂点までの長さ

        public PolyPath(PolylineEntity pl)
        {
            _pl = pl;
            _segs = [.. pl.Segments];
            _cum = new double[_segs.Count + 1];
            for (var i = 0; i < _segs.Count; i++) _cum[i + 1] = _cum[i] + _segs[i].A.DistanceTo(_segs[i].B);
        }

        public override double Length => _cum[^1];
        public override bool Closed => _pl.IsClosed && _pl.Points.Count > 2;
        public override IReadOnlyList<Curve> Curves => [.. _pl.Curves];

        public override double ParamOf(Vec2 p)
        {
            var best = double.MaxValue;
            var result = 0.0;
            for (var i = 0; i < _segs.Count; i++)
            {
                var (a, b) = _segs[i];
                var t = GeoMath.ClosestParamOnSegment(p, a, b);
                var d = p.DistanceTo(a + (b - a) * t);
                if (d < best) (best, result) = (d, _cum[i] + t * (_cum[i + 1] - _cum[i]));
            }
            return result;
        }

        public override Entity Sub(double s0, double s1)
        {
            var points = new List<Vec2> { PointAt(s0) };
            // 区間内にある頂点 (閉じた形状は 2 周目まで) を順に拾う。閉じた形状の最後の点 (s = L) は先頭と同じなので数えない
            var vertexCount = Closed ? _cum.Length - 1 : _cum.Length;
            for (var lap = 0; lap < (Closed ? 2 : 1); lap++)
                for (var i = 0; i < vertexCount; i++)
                {
                    var s = _cum[i] + lap * Length;
                    if (s > s0 + 1e-9 && s < s1 - 1e-9) points.Add(i < _segs.Count ? _segs[i].A : _segs[^1].B);
                }
            points.Add(PointAt(s1));
            return EditUtil.CopyStyle(_pl, new PolylineEntity(_pl.Layer, points));
        }

        private Vec2 PointAt(double s)
        {
            if (Closed && s > Length) s -= Length;
            s = Math.Clamp(s, 0, Length);
            for (var i = 0; i < _segs.Count; i++)
            {
                if (s > _cum[i + 1] && i < _segs.Count - 1) continue;
                var len = _cum[i + 1] - _cum[i];
                var t = len > 0 ? (s - _cum[i]) / len : 0;
                return _segs[i].A + (_segs[i].B - _segs[i].A) * t;
            }
            return _segs[^1].B;
        }
    }
}
