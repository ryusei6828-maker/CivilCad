using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>
/// 座標設定: 図面上の基準点と、その測量座標 (X=北, Y=東) を指定して、図面全体を移動・回転し
/// 図面の座標 = 測量座標 にする。基準点 2 点なら方向も合わせる (1 点なら移動だけ)。
/// </summary>
public sealed class CoordinateSetTool : CadTool
{
    private enum Step { Point1, Survey1, Point2, Survey2 }

    private Step _step;
    private Vec2 _p1, _p2;
    private Vec2 _s1; // 基準点1 の測量座標を CAD 座標 (x=東, y=北) にしたもの
    private Vec2 _cursor;

    public override string Name => "座標設定";
    public override Vec2? BasePoint => _step >= Step.Point2 ? _p1 : null;
    public override bool WantsTextInput => _step is Step.Survey1 or Step.Survey2;

    protected override void OnActivated() => SetPrompt("基準点1 を図面上で指定 (スナップ可)");

    public override bool CanStepBack => _step != Step.Point1;
    public override string? ConfirmLabel => _step == Step.Point2 ? "移動だけ行う (Enter)" : null;

    /// <summary>一つ前の手順へ (測量座標の入力 → 図面上の点の指定 → 一つ前の測量座標の入力)。</summary>
    public override void StepBack()
    {
        switch (_step)
        {
            case Step.Survey1:
                _step = Step.Point1;
                OnActivated();
                break;
            case Step.Point2:
                _step = Step.Survey1;
                SetPrompt("基準点1 の測量座標を「X,Y」で入力 (X=北, Y=東。例: -95537.12,-60760.90)");
                break;
            case Step.Survey2:
                _step = Step.Point2;
                SetPrompt("基準点2 を指定 (方向も合わせる) / Enter で移動だけ行う");
                break;
        }
    }

    public override void OnPoint(Vec2 p)
    {
        switch (_step)
        {
            case Step.Point1:
                _p1 = p;
                _step = Step.Survey1;
                SetPrompt("基準点1 の測量座標を「X,Y」で入力 (X=北, Y=東。例: -95537.12,-60760.90)");
                break;
            case Step.Point2:
                if (p.DistanceTo(_p1) < 1e-6)
                {
                    Canvas.ShowMessage("基準点2 は基準点1 と別の点を指定してください。");
                    return;
                }
                _p2 = p;
                _step = Step.Survey2;
                SetPrompt("基準点2 の測量座標を「X,Y」で入力 (X=北, Y=東)");
                break;
        }
    }

    public override bool OnText(string text)
    {
        if (_step is not (Step.Survey1 or Step.Survey2)) return false;
        if (!TryParseSurvey(text, out var s))
        {
            Canvas.ShowMessage("測量座標は「X,Y」の形で入力してください (X=北, Y=東)。");
            return true;
        }
        if (_step == Step.Survey1)
        {
            _s1 = s;
            _step = Step.Point2;
            SetPrompt("基準点2 を指定 (方向も合わせる) / Enter で移動だけ行う");
        }
        else
        {
            Apply(_p2, s);
        }
        return true;
    }

    public override void OnEnter()
    {
        if (_step == Step.Point2) Apply(null, null);
        else if (_step == Step.Point1) Finish();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    private void Apply(Vec2? p2, Vec2? s2)
    {
        var r = ComputeTransform(_p1, _s1, p2, s2);
        Execute(new RotateTranslateCommand(Doc.Entities.ToList(), _p1, r.Angle, r.Delta, "座標設定"));

        var (x, y) = _s1.ToSurvey();
        var msg = $"座標を設定しました: 基準点1 → X={x:F3}, Y={y:F3}";
        if (p2 != null)
        {
            msg += $"、回転 {r.Angle * 180 / Math.PI:+0.0000;-0.0000;0}°";
            msg += $"  (2点間: 図面 {r.DrawingDistance:F3} m / 測量 {r.SurveyDistance:F3} m, 差 {r.SurveyDistance - r.DrawingDistance:+0.000;-0.000;0.000} m)";
            if (Math.Abs(r.ScaleRatio - 1) > 0.005)
                msg += $"  ※距離が {Math.Abs(r.ScaleRatio - 1) * 100:F1}% 違います。縮尺や点の指定を確認してください (大きさは変えていません)";
        }
        Canvas.ShowMessage(msg + "。Ctrl+Z で元に戻せます。");
        Canvas.ZoomExtents();
        Finish();
    }

    public readonly record struct TransformResult(double Angle, Vec2 Delta, double DrawingDistance, double SurveyDistance)
    {
        public double ScaleRatio => DrawingDistance > 0 ? SurveyDistance / DrawingDistance : 1;
    }

    /// <summary>
    /// p1 → s1 に移すための回転角 (p1 中心) と移動量。p2/s2 があれば p1→p2 の向きを s1→s2 に合わせる。
    /// s1, s2 は CAD 座標 (x=東, y=北) に直した測量座標。
    /// </summary>
    public static TransformResult ComputeTransform(Vec2 p1, Vec2 s1, Vec2? p2, Vec2? s2)
    {
        if (p2 is not Vec2 q2 || s2 is not Vec2 t2) return new(0, s1 - p1, 0, 0);
        var angle = GeoMath.NormalizeSignedAngle((t2 - s1).Angle - (q2 - p1).Angle);
        return new(angle, s1 - p1, q2.DistanceTo(p1), t2.DistanceTo(s1));
    }

    /// <summary>"X,Y" (X=北, Y=東) → CAD 座標。先頭の s は省略可。</summary>
    public static bool TryParseSurvey(string text, out Vec2 point)
    {
        point = default;
        var t = text.Trim().Replace('，', ',').TrimStart('s', 'S');
        var parts = t.Split(',');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;
        point = Vec2.FromSurvey(x, y);
        return true;
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_step == Step.Point1) return;
        DrawMarker(dc, _p1, "1");
        if (_step == Step.Point2) Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, _p1, _cursor);
        if (_step == Step.Survey2)
        {
            Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, _p1, _p2);
            DrawMarker(dc, _p2, "2");
        }
    }

    private void DrawMarker(DrawingContext dc, Vec2 p, string label)
    {
        var c = Canvas.WorldToScreen(p);
        dc.DrawEllipse(null, CadCanvas.PreviewPen, c, 7, 7);
        dc.DrawLine(CadCanvas.PreviewPen, new Point(c.X - 10, c.Y), new Point(c.X + 10, c.Y));
        dc.DrawLine(CadCanvas.PreviewPen, new Point(c.X, c.Y - 10), new Point(c.X, c.Y + 10));
        var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13,
            CadCanvas.PreviewPen.Brush, VisualTreeHelper.GetDpi(Canvas).PixelsPerDip);
        dc.DrawText(ft, new Point(c.X + 9, c.Y - 22));
    }
}
