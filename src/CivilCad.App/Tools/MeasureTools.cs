using System.Globalization;
using System.Windows.Media;
using CivilCad.Alignment;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>計測結果の記入の設定 (作図入力バーで変更。次の計測でも使う)。</summary>
public static class MeasureSettings
{
    /// <summary>計測のあと、結果を文字として図面に置く。</summary>
    public static bool PlaceText { get; set; } = true;

    /// <summary>文字高 (用紙上の mm)。</summary>
    public static double TextHeightMm { get; set; } = 2.5;

    /// <summary>小数点以下の桁数。</summary>
    public static int Decimals
    {
        get => _decimals;
        set => _decimals = Math.Clamp(value, 0, 6);
    }

    private static int _decimals = 3;

    /// <summary>距離なら方向角、面積なら周長も書く。</summary>
    public static bool Detail { get; set; }

    /// <summary>計測結果の単位 (mm / cm / m)。</summary>
    public static LengthUnit Unit { get; set; } = LengthUnit.M;

    public static string Num(double v) => v.ToString("F" + Decimals, CultureInfo.InvariantCulture);

    /// <summary>
    /// CAD 上の長さ (図面の座標の単位 = m) に縮尺を掛けて、選んだ単位で表した値。
    /// 例: 1:100 で CAD 上 0.01 (1cm) → 1m → 100cm。
    /// </summary>
    public static double Length(double cadLength, double scale) => cadLength * scale / Unit.Meters();

    /// <summary>面積は縮尺の 2 乗を掛ける。</summary>
    public static double Area(double cadArea, double scale) => cadArea * scale * scale / (Unit.Meters() * Unit.Meters());

    public static string UnitLabel => Unit.Label();

    public static string DistanceText(Vec2 a, Vec2 b, double scale)
    {
        var d = b - a;
        var text = $"L={Num(Length(d.Length, scale))}{UnitLabel}";
        return Detail ? $"{text}  θ={Angles.ToDms(Angles.ToSurveyAzimuth(d.Angle))}" : text;
    }

    public static string AreaText(double cadArea, double cadPerimeter, double scale)
    {
        var text = $"A={Num(Area(cadArea, scale))}{UnitLabel}²";
        return Detail ? $"{text}  (周長 {Num(Length(cadPerimeter, scale))}{UnitLabel})" : text;
    }

    /// <summary>線の向きに沿った文字の角度 (逆さまにならないよう -90°〜90° に収める)。</summary>
    public static double ReadableAngle(Vec2 a, Vec2 b)
    {
        var ang = GeoMath.NormalizeSignedAngle((b - a).Angle);
        if (ang > Math.PI / 2 + 1e-9) ang -= Math.PI;
        else if (ang <= -Math.PI / 2 + 1e-9) ang += Math.PI;
        return ang;
    }
}

/// <summary>
/// 計測結果を文字として置く手順の共通部分。結果の文字はカーソルに付いて動き、クリックした位置に置く。
/// Enter で置かずに次の計測へ。
/// </summary>
public abstract class MeasureToolBase : CadTool, IStyledDrawTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);

    /// <summary>置こうとしている結果の文字 (置く位置を待っているときだけ)。</summary>
    protected TextEntity? Pending { get; private set; }

    protected Vec2 Cursor { get; private set; }

    /// <summary>計測が終わった: 記入する設定なら文字を置く手順へ、そうでなければすぐ次の計測へ。</summary>
    protected void Measured(string text, double rotation, TextHAlign halign, TextVAlign valign)
    {
        if (!MeasureSettings.PlaceText)
        {
            Restart();
            return;
        }
        Pending = new TextEntity(Doc.CurrentLayer, Cursor, text, Doc.PaperMmToMeters(MeasureSettings.TextHeightMm), rotation)
        {
            HAlign = halign,
            VAlign = valign,
        };
        SetPrompt($"「{text}」を置く位置を指定 (Enter で置かずに次へ / 右クリック > 一つ戻すで計測に戻る)");
    }

    /// <summary>次の計測を始める (点などを消す)。</summary>
    protected abstract void Restart();

    /// <summary>文字を置く手順から、計測の最後の手順に戻す。</summary>
    protected abstract void BackToMeasuring();

    public override void OnPoint(Vec2 p)
    {
        if (Pending is { } t)
        {
            t.Position = p;
            Execute(new AddEntitiesCommand([Styled(t)], $"{Name}の記入"));
            Pending = null;
            Restart();
            return;
        }
        OnMeasurePoint(p);
    }

    protected abstract void OnMeasurePoint(Vec2 p);

    public override void OnMouseMove(Vec2 p) => Cursor = p;

    public override void OnEnter()
    {
        if (Pending != null)
        {
            Pending = null;
            Restart();
            return;
        }
        OnMeasureEnter();
    }

    protected virtual void OnMeasureEnter() { }

    public override bool CanStepBack => Pending != null || CanStepBackMeasure;
    protected abstract bool CanStepBackMeasure { get; }

    public override void StepBack()
    {
        if (Pending != null)
        {
            Pending = null;
            BackToMeasuring();
        }
        else
        {
            StepBackMeasure();
        }
    }

    protected abstract void StepBackMeasure();

    public override string? ConfirmLabel => Pending != null ? "記入しないで次へ (Enter)" : MeasureConfirmLabel;
    protected virtual string? MeasureConfirmLabel => null;

    /// <summary>計測中の図形と、置こうとしている文字 (カーソル位置) を描く。</summary>
    public override void DrawPreview(DrawingContext dc)
    {
        DrawMeasurePreview(dc);
        if (Pending is { } t)
        {
            t.Position = Cursor;
            Canvas.DrawEntity(dc, t, PreviewColor);
        }
    }

    protected abstract void DrawMeasurePreview(DrawingContext dc);
}

/// <summary>2点間の距離と方向角 (測量: 北から時計回り)。結果は線に沿った文字で記入できる。</summary>
public sealed class DistanceTool : MeasureToolBase
{
    private Vec2? _first;
    private Vec2? _second;

    public override string Name => "距離";
    public override Vec2? BasePoint => Pending == null ? _first : null;
    protected override bool CanStepBackMeasure => _first != null;
    protected override void OnActivated() => SetPrompt("1点目を指定");

    protected override void StepBackMeasure()
    {
        _first = null;
        OnActivated();
    }

    protected override void Restart()
    {
        _first = _second = null;
        OnActivated();
    }

    protected override void BackToMeasuring()
    {
        _second = null;
        SetPrompt("2点目を指定");
    }

    protected override void OnMeasurePoint(Vec2 p)
    {
        if (_first is not Vec2 a)
        {
            _first = p;
            SetPrompt("2点目を指定");
            return;
        }
        if (p.DistanceTo(a) < 1e-12) return;
        _second = p;
        var d = p - a;
        var s = Doc.ScaleDenominator;
        var u = MeasureSettings.UnitLabel;
        string L(double v) => $"{MeasureSettings.Num(MeasureSettings.Length(v, s))} {u}";
        Canvas.ShowMessage($"距離 = {L(d.Length)}   ΔX(北) = {L(d.Y)}  ΔY(東) = {L(d.X)}   方向角 = {Angles.ToDms(Angles.ToSurveyAzimuth(d.Angle))}" +
                           $"   (CAD上 {d.Length:F3} × 縮尺 {Doc.ScaleText})");
        Measured(MeasureSettings.DistanceText(a, p, s), MeasureSettings.ReadableAngle(a, p), TextHAlign.Center, TextVAlign.Bottom);
    }

    protected override void DrawMeasurePreview(DrawingContext dc)
    {
        if (_first is Vec2 a) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, a, _second ?? Cursor);
    }
}

/// <summary>点を順に指定して面積 (座標法) と周長を求める。結果は文字で記入できる。</summary>
public sealed class AreaTool : MeasureToolBase
{
    private readonly List<Vec2> _points = [];
    private bool _closed;

    public override string Name => "面積";
    public override Vec2? BasePoint => _points.Count > 0 && Pending == null ? _points[^1] : null;
    protected override bool CanStepBackMeasure => _points.Count > 0;
    protected override string? MeasureConfirmLabel => _points.Count >= 3 ? "面積を計算 (Enter)" : null;
    protected override void OnActivated() => SetPrompt("頂点を順に指定 (Enter で計算)");

    protected override void StepBackMeasure()
    {
        if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
    }

    protected override void Restart()
    {
        _points.Clear();
        _closed = false;
        OnActivated();
    }

    protected override void BackToMeasuring()
    {
        _closed = false;
        OnActivated();
    }

    protected override void OnMeasurePoint(Vec2 p) => _points.Add(p);

    protected override void OnMeasureEnter()
    {
        if (_points.Count == 0)
        {
            Finish();
            return;
        }
        if (_points.Count < 3)
        {
            Canvas.ShowMessage("面積は 3 点以上で指定してください。");
            return;
        }
        double sum = 0, perimeter = 0;
        for (var i = 0; i < _points.Count; i++)
        {
            var a = _points[i];
            var b = _points[(i + 1) % _points.Count];
            sum += a.Cross(b);
            perimeter += a.DistanceTo(b);
        }
        var area = Math.Abs(sum) / 2;
        var s = Doc.ScaleDenominator;
        var u = MeasureSettings.UnitLabel;
        Canvas.ShowMessage($"面積 = {MeasureSettings.Num(MeasureSettings.Area(area, s))} {u}²   周長 = {MeasureSettings.Num(MeasureSettings.Length(perimeter, s))} {u}" +
                           $"   ({_points.Count} 点、CAD上 {area:F3} / {perimeter:F3} × 縮尺 {Doc.ScaleText})");
        _closed = true;
        Measured(MeasureSettings.AreaText(area, perimeter, s), 0, TextHAlign.Center, TextVAlign.Middle);
    }

    protected override void DrawMeasurePreview(DrawingContext dc)
    {
        if (_points.Count == 0) return;
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, _closed ? _points : [.. _points, Cursor], true);
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
