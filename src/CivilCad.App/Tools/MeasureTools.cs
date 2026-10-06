using System.Windows.Media;
using CivilCad.Alignment;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>2点間の距離と方向角 (測量: 北から時計回り)。</summary>
public sealed class DistanceTool : CadTool
{
    private Vec2? _first;
    private Vec2 _cursor;

    public override string Name => "距離";
    public override Vec2? BasePoint => _first;
    public override bool CanStepBack => _first != null;
    protected override void OnActivated() => SetPrompt("1点目を指定");

    public override void StepBack()
    {
        _first = null;
        OnActivated();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_first is not Vec2 a)
        {
            _first = p;
            SetPrompt("2点目を指定");
            return;
        }
        var d = p - a;
        Canvas.ShowMessage($"距離 = {d.Length:F3} m   ΔX(北) = {d.Y:F3}  ΔY(東) = {d.X:F3}   方向角 = {Angles.ToDms(Angles.ToSurveyAzimuth(d.Angle))}");
        _first = null;
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_first is Vec2 a) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, a, _cursor);
    }
}

/// <summary>点を順に指定して面積 (座標法) と周長を求める。</summary>
public sealed class AreaTool : CadTool
{
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;

    public override string Name => "面積";
    public override Vec2? BasePoint => _points.Count > 0 ? _points[^1] : null;
    public override bool CanStepBack => _points.Count > 0;
    public override string? ConfirmLabel => _points.Count >= 3 ? "面積を計算 (Enter)" : null;
    protected override void OnActivated() => SetPrompt("頂点を順に指定 (Enter で計算)");

    public override void StepBack()
    {
        if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
    }

    public override void OnPoint(Vec2 p) => _points.Add(p);
    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void OnEnter()
    {
        if (_points.Count >= 3)
        {
            double sum = 0, perimeter = 0;
            for (var i = 0; i < _points.Count; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % _points.Count];
                sum += a.Cross(b);
                perimeter += a.DistanceTo(b);
            }
            Canvas.ShowMessage($"面積 = {Math.Abs(sum) / 2:F3} m²   周長 = {perimeter:F3} m   ({_points.Count} 点)");
        }
        Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_points.Count > 0) Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, [.. _points, _cursor], true);
    }
}

/// <summary>クリック位置の測点と幅 (中心線からの離れ) を表示。</summary>
public sealed class StationQueryTool(HorizontalAlignment alignment, double pitch) : CadTool
{
    private Vec2 _cursor;

    public override string Name => "測点照会";
    protected override void OnActivated() => SetPrompt("照会する点を指定 (Esc で終了)");

    public override void OnPoint(Vec2 p)
    {
        var (st, off) = alignment.Project(p);
        var (x, y) = p.ToSurvey();
        var side = off >= 0 ? "右" : "左";
        Canvas.ShowMessage($"{Station.Format(st, pitch)} (追加距離 {st:F3})   幅 {side} {Math.Abs(off):F3} m   X = {x:F3}  Y = {y:F3}");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;
    public override void OnEnter() { }

    public override void DrawPreview(DrawingContext dc)
    {
        var (st, _) = alignment.Project(_cursor);
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, alignment.PointAt(st), _cursor);
    }
}
