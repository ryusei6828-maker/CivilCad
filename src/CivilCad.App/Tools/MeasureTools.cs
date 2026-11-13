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

    /// <summary>面積の測り方: false = 点を順に指定、true = 境界の図形で囲まれた部分をクリック。</summary>
    public static bool AreaByRegion { get; set; }

    /// <summary>面積: 測った部分を、塗りつぶし (ハッチング) として図面に残す (レイヤ「面積-塗り」、透明度 50%)。</summary>
    public static bool KeepAreaFill { get; set; }

    /// <summary>距離なら方向角、面積なら周長も書く。</summary>
    public static bool Detail { get; set; }

    /// <summary>測点照会: 測点に加えて、左右と幅も書く。</summary>
    public static bool StationOffset { get; set; } = true;

    /// <summary>測点照会: 文字を置くとき、路線上の点から照会した点への線も一緒に置く。</summary>
    public static bool StationMark { get; set; }

    /// <summary>計測結果の単位 (mm / cm / m)。</summary>
    public static LengthUnit Unit { get; set; } = LengthUnit.M;

    public static string Num(double v) => v.ToString("F" + Decimals, CultureInfo.InvariantCulture);

    /// <summary>
    /// 図面上の長さ (座標の単位 = 実寸 m) を、選んだ単位で表した値。図面は実寸で描いているので縮尺は掛けない
    /// (縮尺は用紙に印刷するときの大きさにだけ使う。例: 1:100 の A3 図枠は 38m)。
    /// </summary>
    public static double Length(double cadLength) => cadLength / Unit.Meters();

    /// <summary>図面上の面積 (実寸 m²) を、選んだ単位で表した値。</summary>
    public static double Area(double cadArea) => cadArea / (Unit.Meters() * Unit.Meters());

    public static string UnitLabel => Unit.Label();

    /// <param name="surveyRotation">測量座標の向き (方向角を測量座標で出す)。</param>
    /// <param name="verticalFactor">縦の長さの倍率 (縦横で縮尺が違うシート)。1 以外なら実際の長さと高低差・勾配を書く。</param>
    public static string DistanceText(Vec2 a, Vec2 b, double surveyRotation = 0, double verticalFactor = 1)
    {
        if (Math.Abs(verticalFactor - 1) > 1e-12)
        {
            var r = new Vec2(b.X - a.X, (b.Y - a.Y) * verticalFactor);
            var t = $"L={Num(Length(r.Length))}{UnitLabel}";
            return Detail ? $"{t}  ΔH={Num(Length(r.Y))}{UnitLabel}" + (Math.Abs(r.X) > 1e-9 ? $"  i={r.Y / Math.Abs(r.X) * 100:0.###}%" : "") : t;
        }
        var d = (b - a).Rotate(surveyRotation);
        var text = $"L={Num(Length(d.Length))}{UnitLabel}";
        return Detail ? $"{text}  θ={Angles.ToDms(Angles.ToSurveyAzimuth(d.Angle))}" : text;
    }

    public static string AreaText(double cadArea, double cadPerimeter)
    {
        var text = $"A={Num(Area(cadArea))}{UnitLabel}²";
        return Detail ? $"{text}  (周長 {Num(Length(cadPerimeter))}{UnitLabel})" : text;
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

    /// <summary>置こうとしている文字を取り下げる (測り方を切り替えたとき)。</summary>
    protected void CancelPending() => Pending = null;

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
            var added = new List<Entity> { Styled(t) };
            added.AddRange(ExtraEntities().Select(Styled));
            Execute(new AddEntitiesCommand(added, $"{Name}の記入"));
            Pending = null;
            Restart();
            return;
        }
        OnMeasurePoint(p);
    }

    /// <summary>文字と一緒に置く図形 (測点照会の印の線など)。</summary>
    protected virtual IEnumerable<Entity> ExtraEntities() => [];

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
        var u = MeasureSettings.UnitLabel;
        string L(double v) => $"{MeasureSettings.Num(MeasureSettings.Length(v))} {u}";
        if (Doc.HasVerticalScale)
        {
            // 縦横で縮尺が違うシート (縦断図など): 縦は縦の縮尺で直して、斜めの長さ・水平距離・高低差・勾配
            var r = Doc.ToRealDelta(p - a);
            Canvas.ShowMessage($"距離 = {L(r.Length)}   水平 = {L(Math.Abs(r.X))}  高低差 = {L(r.Y)}" +
                               (Math.Abs(r.X) > 1e-9 ? $"   勾配 = {r.Y / Math.Abs(r.X) * 100:0.###} %" : "") + $"   ({Doc.ScaleText})");
        }
        else
        {
            var d = (p - a).Rotate(Doc.SurveyRotation); // ΔX・ΔY・方向角は測量座標の向きで (長さは同じ)
            Canvas.ShowMessage($"距離 = {L(d.Length)}   ΔX(北) = {L(d.Y)}  ΔY(東) = {L(d.X)}   方向角 = {Angles.ToDms(Angles.ToSurveyAzimuth(d.Angle))}");
        }
        Measured(MeasureSettings.DistanceText(a, p, Doc.SurveyRotation, Doc.VerticalFactor), MeasureSettings.ReadableAngle(a, p), TextHAlign.Center, TextVAlign.Bottom);
    }

    protected override void DrawMeasurePreview(DrawingContext dc)
    {
        if (_first is Vec2 a) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, a, _second ?? Cursor);
    }
}

/// <summary>
/// 面積 (AA)。測り方は 2 通り (入力バーで切替):
/// 点を指定 = 頂点を順にクリックして座標法で求める。
/// 囲まれた部分 = 境界にする図形 (線・ポリライン・円弧・円。1 つの閉じた図形でなくてもよい) を指定し、囲まれた部分の内側をクリックする。
/// </summary>
public sealed class AreaTool : MeasureToolBase
{
    private static readonly RgbColor BoundaryColor = new(0xFF, 0xAA, 0x33);
    private static readonly Brush MeasuredFillBrush = FrozenBrush(Color.FromArgb(0x59, 0x66, 0xCC, 0xFF)); // 薄い水色 (不透明度 35%)

    private static Brush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>図面に残す塗りつぶしのレイヤ名と色。</summary>
    public const string FillLayerName = "面積-塗り";
    private static readonly RgbColor FillLayerColor = new(0x99, 0xCC, 0xFF);

    /// <summary>「測った部分を塗りつぶして残す」がオンなら、測った部分 (最初が外形、残りは中抜き) を塗りつぶしとして追加する。</summary>
    private void KeepFill(IEnumerable<IReadOnlyList<Vec2>> loops)
    {
        if (!MeasureSettings.KeepAreaFill) return;
        var layer = Doc.GetOrCreateLayer(FillLayerName, FillLayerColor);
        var hatch = new HatchEntity(layer, loops, [], true) { TransparencyPercent = 50 };
        if (hatch.Loops.Count == 0) return;
        Execute(new AddEntitiesCommand([hatch], "面積の塗りつぶし"));
    }

    private readonly List<Vec2> _points = [];
    private bool _closed;

    // 囲まれた部分モード
    private readonly List<Entity> _boundary = [];
    private bool _picking = true;      // true = 境界の指定中、false = 内側のクリック待ち
    private bool _useAll;              // 何も選ばずに Enter: 表示中のすべての図形を境界にする
    private Region? _region;
    private readonly VisibleRegionCache _allRegions = new();

    private static bool ByRegion => MeasureSettings.AreaByRegion;

    public override string Name => "面積";
    public override Vec2? BasePoint => !ByRegion && _points.Count > 0 && Pending == null ? _points[^1] : null;
    /// <summary>図形を選ぶ間と内側をクリックする間は、スナップしない (図形そのものをクリックする)。</summary>
    public override bool WantsSnap => !ByRegion || Pending != null;
    protected override bool CanStepBackMeasure => ByRegion ? !_picking || _boundary.Count > 0 : _points.Count > 0;
    protected override string? MeasureConfirmLabel => ByRegion
        ? (_picking ? (_boundary.Count > 0 ? "境界を確定 (Enter)" : "すべての図形を境界に (Enter)") : "境界の指定に戻る (Enter)")
        : _points.Count >= 3 ? "面積を計算 (Enter)" : null;

    /// <summary>起動時と、測り方を切り替えたとき: 状態を最初に戻す。</summary>
    protected override void OnActivated()
    {
        CancelPending();
        _points.Clear();
        _closed = false;
        _boundary.Clear();
        _useAll = false;
        _region = null;
        _picking = true;
        if (ByRegion)
        {
            // 選択中の図形は境界に入れておく (橙色)。ただし境界の指定は続ける (1 本だけでは囲めないので、足してから Enter)
            _boundary.AddRange(Canvas.Selection.Where(e => ToCurves(e).Any()));
            PromptRegion();
        }
        else SetPrompt("頂点を順に指定 (Enter で計算)");
    }

    private void PromptRegion()
    {
        SetPrompt(_picking
            ? _boundary.Count > 0
                ? $"境界にする図形をクリック (境界 {_boundary.Count} 個。もう一度クリックで外す。Enter で確定)"
                : "境界にする図形をクリック (もう一度クリックで外す。Enter で確定、何も選ばずに Enter なら表示中のすべての図形)"
            : "囲まれた部分の内側をクリック (右クリック > 一つ戻すで境界の指定に戻る)");
    }

    protected override void StepBackMeasure()
    {
        if (!ByRegion)
        {
            if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
            return;
        }
        _region = null;
        if (!_picking)
        {
            _picking = true;
            _useAll = false;
        }
        else if (_boundary.Count > 0) _boundary.RemoveAt(_boundary.Count - 1);
        PromptRegion();
    }

    protected override void Restart()
    {
        if (ByRegion)
        {
            // 同じ境界のまま、別の部分を続けて測る (測った部分の塗りは、次のクリック・境界の指定に戻るまで見せておく)
            PromptRegion();
            return;
        }
        _points.Clear();
        _closed = false;
        SetPrompt("頂点を順に指定 (Enter で計算)");
    }

    protected override void BackToMeasuring()
    {
        if (ByRegion)
        {
            _region = null;
            PromptRegion();
            return;
        }
        _closed = false;
        SetPrompt("頂点を順に指定 (Enter で計算)");
    }

    protected override void OnMeasurePoint(Vec2 p)
    {
        if (!ByRegion)
        {
            _points.Add(p);
            return;
        }
        if (_picking)
        {
            if (Canvas.HitTest(p) is not { } hit) return;
            if (_boundary.Remove(hit)) return;
            if (!ToCurves(hit).Any())
            {
                Canvas.ShowMessage("面積: 線・ポリライン・円弧・円が境界にできます。");
                return;
            }
            _boundary.Add(hit);
            return;
        }
        MeasureRegion(p);
    }

    private void MeasureRegion(Vec2 p)
    {
        var region = _useAll
            ? _allRegions.Get(Doc).Find(p)
            : RegionFinder.Find(_boundary.Where(e => Doc.Entities.Contains(e)).SelectMany(ToCurves), p);
        if (region == null)
        {
            _region = null;
            Canvas.ShowMessage(_useAll
                ? "囲まれた部分が見つかりません (表示中の図形では閉じていない)"
                : $"囲まれた部分が見つかりません。境界 {_boundary.Count} 個では閉じていません (右クリック > 一つ戻すで境界の図形を足す)");
            return;
        }
        double perimeter = region.Perimeter;
        if (Doc.HasVerticalScale)
        {
            // 縦横で縮尺が違えば、縦を直した長さで周長を出す (円弧は細かい弦で近似)
            perimeter = region.Loops.Sum(l => Enumerable.Range(0, l.Count).Sum(i => Doc.ToRealDelta(l[(i + 1) % l.Count] - l[i]).Length));
        }
        var area = region.Area * Doc.VerticalFactor;
        var u = MeasureSettings.UnitLabel;
        Canvas.ShowMessage($"面積 = {MeasureSettings.Num(MeasureSettings.Area(area))} {u}²   周長 = {MeasureSettings.Num(MeasureSettings.Length(perimeter))} {u}   (囲まれた部分)");
        _region = region;
        KeepFill(region.Loops);
        Measured(MeasureSettings.AreaText(area, perimeter), 0, TextHAlign.Center, TextVAlign.Middle);
    }

    protected override void OnMeasureEnter()
    {
        if (ByRegion)
        {
            if (_picking)
            {
                _useAll = _boundary.Count == 0;
                _picking = false;
            }
            else
            {
                // 境界の指定に戻る (空から選び直す)
                _boundary.Clear();
                _useAll = false;
                _picking = true;
            }
            _region = null;
            PromptRegion();
            return;
        }
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
            perimeter += Doc.ToRealDelta(b - a).Length; // 縦横で縮尺が違えば縦を直した長さ
        }
        var area = Math.Abs(sum) / 2 * Doc.VerticalFactor; // 縦横で縮尺が違えば縦の倍率を掛ける
        var u = MeasureSettings.UnitLabel;
        Canvas.ShowMessage($"面積 = {MeasureSettings.Num(MeasureSettings.Area(area))} {u}²   周長 = {MeasureSettings.Num(MeasureSettings.Length(perimeter))} {u}" +
                           $"   ({_points.Count} 点)");
        _closed = true;
        KeepFill([_points.ToList()]);
        Measured(MeasureSettings.AreaText(area, perimeter), 0, TextHAlign.Center, TextVAlign.Middle);
    }

    protected override void DrawMeasurePreview(DrawingContext dc)
    {
        if (ByRegion)
        {
            foreach (var e in _boundary) Canvas.DrawEntity(dc, e, BoundaryColor);
            if (_region != null)
            {
                Canvas.DrawWorldRegionFill(dc, MeasuredFillBrush, _region.Loops);
                foreach (var loop in _region.Loops) Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, [.. loop], true);
            }
            return;
        }
        if (_points.Count == 0) return;
        if (_closed) Canvas.DrawWorldRegionFill(dc, MeasuredFillBrush, [_points]);
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, _closed ? _points : [.. _points, Cursor], true);
    }

    /// <summary>図形を、囲まれた部分を作る境界の曲線にする (対象外の図形は空)。</summary>
    public static IEnumerable<BoundaryCurve> ToCurves(Entity e) => BoundaryCurves.FromEntity(e);
}

/// <summary>クリック位置の測点と幅 (中心線からの離れ) を表示。結果は路線に沿った文字で記入できる。</summary>
public sealed class StationQueryTool(HorizontalAlignment alignment, double pitch) : MeasureToolBase
{
    private Vec2? _foot;    // 路線上の点 (図面座標)
    private Vec2? _queried; // 照会した点 (図面座標)

    public override string Name => "測点照会";
    public override Vec2? BasePoint => null;
    protected override bool CanStepBackMeasure => false;
    protected override void OnActivated() => SetPrompt("照会する点を指定 (Esc で終了)");
    protected override void StepBackMeasure() { }

    protected override void Restart()
    {
        _foot = _queried = null;
        OnActivated();
    }

    protected override void BackToMeasuring()
    {
        _foot = _queried = null;
        OnActivated();
    }

    protected override void OnMeasurePoint(Vec2 p)
    {
        var survey = Doc.ToSurveyCad(p); // 路線は測量座標で持っている
        var (st, off) = alignment.Project(survey);
        var (x, y) = survey.ToSurvey();
        var side = off >= 0 ? "右" : "左";
        Canvas.ShowMessage($"{Station.Format(st, pitch)} (追加距離 {st:F3})   幅 {side} {Math.Abs(off):F3} m   X = {x:F3}  Y = {y:F3}");

        var foot = Doc.FromSurveyCad(alignment.PointAt(st));
        _foot = foot;
        _queried = p;
        var text = Station.Format(st, pitch, MeasureSettings.Decimals);
        if (MeasureSettings.StationOffset)
            text += $"  {side} {MeasureSettings.Num(MeasureSettings.Length(Math.Abs(off)))}{MeasureSettings.UnitLabel}";
        // 文字は、その測点での路線の接線の向きに沿わせる
        var (pt, dir) = alignment.Evaluate(st);
        var a = Doc.FromSurveyCad(pt);
        var b = Doc.FromSurveyCad(pt + Vec2.FromAngle(dir));
        Measured(text, MeasureSettings.ReadableAngle(a, b), TextHAlign.Left, TextVAlign.Bottom);
    }

    protected override IEnumerable<Entity> ExtraEntities()
    {
        if (MeasureSettings.StationMark && _foot is Vec2 f && _queried is Vec2 q && f.DistanceTo(q) >= 1e-6)
            yield return new LineEntity(Doc.CurrentLayer, f, q);
    }

    protected override void DrawMeasurePreview(DrawingContext dc)
    {
        if (Pending != null)
        {
            if (_foot is Vec2 f && _queried is Vec2 q) Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, f, q);
            return;
        }
        var (st, _) = alignment.Project(Doc.ToSurveyCad(Cursor));
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, Doc.FromSurveyCad(alignment.PointAt(st)), Cursor);
    }
}
