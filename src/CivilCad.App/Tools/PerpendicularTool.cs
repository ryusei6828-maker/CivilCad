using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 垂線: 基準の線 (線・ポリライン・円・円弧) を選び、その上の点 (足) を指定して、そこから直角に線を引く。
/// 円・円弧では中心を通る向き (法線) に引く。長さはマウスで決めるか、作図入力バーで固定する。
/// 1 本引いたら同じ基準の線のまま続けて引ける (Esc で終了)。
/// </summary>
public sealed class PerpendicularTool : CadTool, IStyledDrawTool
{
    private static readonly RgbColor BaseColor = new(0xFF, 0xA0, 0x40);

    private Entity? _base;
    private (Vec2 A, Vec2 B)? _segment;   // 基準が線分のとき
    private (Vec2 Center, double R)? _round; // 基準が円・円弧のとき
    private Vec2? _foot;
    private Vec2 _cursor;

    public override string Name => "垂線";
    public override Vec2? BasePoint => _foot;

    /// <summary>垂線の長さ (m)。null ならマウスで決める。</summary>
    public static double? FixedLength { get; set; }

    public override bool CanStepBack => _base != null;

    protected override void OnActivated() =>
        SetPrompt(_base == null ? "垂線を立てる基準の線 (線・ポリライン・円・円弧) をクリック"
            : _foot == null ? "基準の線上の点 (垂線の足) を指定 (スナップ可。線の延長上も可)"
            : FixedLength is double l ? $"引く側を指定 (長さ {l:0.###} m)" : "垂線の終点を指定 (長さはマウス、または作図入力バーで固定)");

    /// <summary>垂線の足 → 基準の線の選び直し、の順に戻る。</summary>
    public override void StepBack()
    {
        if (_foot != null) _foot = null;
        else ClearBase();
        OnActivated();
    }

    private void ClearBase()
    {
        _base = null;
        _segment = null;
        _round = null;
    }

    public override Vec2 ConstrainCursor(Vec2 p)
    {
        if (_base == null) return p;
        if (_foot is not Vec2 foot) return Foot(p);
        var n = Normal(foot, p);
        var t = n.Dot(p - foot);
        if (FixedLength is double l) t = t < 0 ? -l : l;
        return foot + n * t;
    }

    public override void OnPoint(Vec2 p)
    {
        if (_base == null)
        {
            PickBase(p);
            return;
        }
        if (_foot is not Vec2 foot)
        {
            _foot = Foot(p);
            OnActivated();
            return;
        }
        var end = ConstrainCursor(p);
        if (end.DistanceTo(foot) < 1e-9) return;
        var line = Styled(new LineEntity(Doc.CurrentLayer, foot, end));
        var cmd = new AddEntitiesCommand([line], "垂線");
        Execute(cmd);
        _foot = null; // 同じ基準の線で続けて引ける
        OnActivated();
    }

    private void PickBase(Vec2 p)
    {
        var hit = Canvas.HitTest(p);
        switch (hit)
        {
            case LineEntity l when l.Start.DistanceTo(l.End) > 1e-9:
                _segment = (l.Start, l.End);
                break;
            case PolylineEntity pl when pl.Segments.Any():
                var seg = pl.Segments.Where(s => s.A.DistanceTo(s.B) > 1e-9).MinBy(s => GeoMath.DistanceToSegment(p, s.A, s.B));
                _segment = (seg.A, seg.B);
                break;
            case CircleEntity c:
                _round = (c.Center, c.Radius);
                break;
            case ArcEntity a:
                _round = (a.Center, a.Radius);
                break;
            default:
                Canvas.ShowMessage("線・ポリライン・円・円弧をクリックしてください。");
                return;
        }
        _base = hit;
        OnActivated();
    }

    /// <summary>基準の線上で p にいちばん近い点 (線分は延長線上も可)。</summary>
    private Vec2 Foot(Vec2 p)
    {
        if (_segment is { } seg)
        {
            var (a, b) = seg;
            var d = (b - a).Normalized();
            return a + d * d.Dot(p - a);
        }
        var (c, r) = _round!.Value;
        var u = (p - c).Normalized();
        return c + (u == Vec2.Zero ? new Vec2(1, 0) : u) * r;
    }

    /// <summary>足での垂線の向き (単位ベクトル)。p の側を正にはしない (符号は呼び出し側で決める)。</summary>
    private Vec2 Normal(Vec2 foot, Vec2 p)
    {
        if (_segment is { } seg) return (seg.B - seg.A).Normalized().LeftNormal;
        var (c, _) = _round!.Value;
        var u = (foot - c).Normalized();
        return u == Vec2.Zero ? new Vec2(1, 0) : u;
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_base != null && Doc.Entities.Contains(_base)) Canvas.DrawEntity(dc, _base, BaseColor, true);
        if (_base == null) return;
        if (_foot is Vec2 foot)
        {
            Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, foot, _cursor);
            DrawRightAngleMark(dc, foot, _cursor);
        }
        else
        {
            // 足の位置を示す
            var f = Canvas.WorldToScreen(_cursor);
            dc.DrawEllipse(null, CadCanvas.PreviewPen, f, 4, 4);
        }
    }

    /// <summary>足に直角の印 (画面上 10px) を描く。</summary>
    private void DrawRightAngleMark(DrawingContext dc, Vec2 foot, Vec2 end)
    {
        var up = end - foot;
        if (up.Length < 1e-12) return;
        var s = 10 / Canvas.Scale;
        var n = up.Normalized();
        var t = _segment is { } seg ? (seg.B - seg.A).Normalized() : n.LeftNormal;
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewDashPen, [foot + t * s, foot + t * s + n * s, foot + n * s], false);
    }
}
