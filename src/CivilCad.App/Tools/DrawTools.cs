using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

public sealed class LineTool : CadTool, ILengthAngleInput, IStyledDrawTool
{
    // 指定した点と、その点で作った線 (始点なら null)。「一つ戻す」で後ろから取り消す
    private readonly List<(Vec2 Point, AddEntitiesCommand? Command, LineEntity? Line)> _steps = [];
    private Vec2 _cursor;

    public override string Name => "線";
    public override Vec2? BasePoint => _steps.Count > 0 ? _steps[^1].Point : null;
    public double? FixedLength { get => DrawInputMemory.Length; set => DrawInputMemory.Length = value; }
    public double? FixedAngleDeg { get => DrawInputMemory.AngleDeg; set => DrawInputMemory.AngleDeg = value; }
    public (double Length, double AngleDeg)? Live => BasePoint is Vec2 b ? LengthAngle.Measure(b, _cursor) : null;
    public override bool CanStepBack => _steps.Count > 0;
    public override string? ConfirmLabel => _steps.Count > 0 ? "この線を終えて次の線へ (Enter)" : null;

    protected override void OnActivated() =>
        SetPrompt(_steps.Count == 0 ? "始点を指定 (Esc で終了)" : "次の点を指定 (Enter でこの線を終えて次の線へ / Esc で終了)");

    public override Vec2 ConstrainCursor(Vec2 p) =>
        BasePoint is Vec2 b ? LengthAngle.Apply(b, p, FixedLength, FixedAngleDeg) : p;

    public override void OnPoint(Vec2 p)
    {
        if (BasePoint is Vec2 last)
        {
            if (last == p) return;
            var line = Styled(new LineEntity(Doc.CurrentLayer, last, p));
            var cmd = new AddEntitiesCommand([line], "線");
            Execute(cmd);
            _steps.Add((p, cmd, line));
        }
        else
        {
            _steps.Add((p, null, null));
        }
        OnActivated();
    }

    /// <summary>Enter: 引いている線を終えて、次の線の始点の指定へ (続けて描ける)。何も引いていなければコマンドを終える。</summary>
    public override void OnEnter()
    {
        if (_steps.Count == 0)
        {
            Finish();
            return;
        }
        _steps.Clear();
        OnActivated();
    }

    /// <summary>最後の点を取り消す (その点で引いた線も消す)。</summary>
    public override void StepBack()
    {
        if (_steps.Count == 0) return;
        var (_, cmd, line) = _steps[^1];
        _steps.RemoveAt(_steps.Count - 1);
        if (cmd != null) Retract(cmd, [line!]);
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (BasePoint is Vec2 last) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, last, _cursor);
    }
}

public sealed class PolylineTool : CadTool, ILengthAngleInput, IStyledDrawTool
{
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;

    public override string Name => "ポリライン";
    public override Vec2? BasePoint => _points.Count > 0 ? _points[^1] : null;
    public double? FixedLength { get => DrawInputMemory.Length; set => DrawInputMemory.Length = value; }
    public double? FixedAngleDeg { get => DrawInputMemory.AngleDeg; set => DrawInputMemory.AngleDeg = value; }
    public (double Length, double AngleDeg)? Live => BasePoint is Vec2 b ? LengthAngle.Measure(b, _cursor) : null;
    public override bool CanStepBack => _points.Count > 0;
    public override string? ConfirmLabel => _points.Count >= 2 ? "確定 (Enter)" : null;

    public override IEnumerable<(string Label, Action Run)> MenuItems =>
        _points.Count >= 3 ? [("閉じて確定 (C)", () => Commit(true))] : [];

    protected override void OnActivated() =>
        SetPrompt(_points.Count == 0 ? "始点を指定" : "次の点を指定 (C = 閉じる / Enter で確定 / 右クリックで一つ戻す・やり直し)");

    public override Vec2 ConstrainCursor(Vec2 p) =>
        BasePoint is Vec2 b ? LengthAngle.Apply(b, p, FixedLength, FixedAngleDeg) : p;

    public override void OnPoint(Vec2 p)
    {
        if (_points.Count > 0 && _points[^1] == p) return;
        _points.Add(p);
        OnActivated();
    }

    public override void StepBack()
    {
        if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override bool OnText(string text)
    {
        if (!text.Trim().Equals("C", StringComparison.OrdinalIgnoreCase)) return false;
        Commit(true);
        return true;
    }

    /// <summary>Enter: 描いている途中ならそのポリラインを確定して次へ。何も描いていなければコマンドを終える。</summary>
    public override void OnEnter()
    {
        if (_points.Count == 0) Finish();
        else Commit(false);
    }

    private void Commit(bool closed)
    {
        if (_points.Count >= 2)
            Execute(new AddEntitiesCommand([Styled(new PolylineEntity(Doc.CurrentLayer, _points, closed && _points.Count > 2))], "ポリライン"));
        // 続けて次のポリラインを描ける (Esc で終了)
        _points.Clear();
        OnActivated();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_points.Count == 0) return;
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, [.. _points, _cursor], false);
    }
}

public sealed class CircleTool : CadTool, IRadiusInput, IStyledDrawTool
{
    private Vec2? _center;
    private Vec2 _cursor;

    public override string Name => "円";
    public override Vec2? BasePoint => _center;
    public double? LiveRadius => _center is Vec2 c ? c.DistanceTo(_cursor) : null;

    /// <summary>半径を固定すると、中心をクリックしただけで円ができる。</summary>
    public double? FixedRadius
    {
        get => DrawInputMemory.Radius;
        set
        {
            DrawInputMemory.Radius = value;
            if (Canvas != null) OnActivated();
        }
    }

    protected override void OnActivated() =>
        SetPrompt(_center != null ? "半径を入力、または円周上の点を指定"
            : FixedRadius is double r ? $"中心点を指定 (半径 {r:0.###} m で作図)" : "中心点を指定");

    public override bool CanStepBack => _center != null;

    public override void StepBack()
    {
        _center = null;
        OnActivated();
    }

    public override void OnPoint(Vec2 p)
    {
        if (_center is Vec2 c) Create(c, c.DistanceTo(p));
        else if (FixedRadius is double r) Create(p, r);
        else
        {
            _center = p;
            SetPrompt("半径を入力、または円周上の点を指定");
        }
    }

    public override bool OnText(string text)
    {
        if (_center is not Vec2 c || !TryParseNumber(text, out var r)) return false;
        Create(c, r);
        return true;
    }

    private void Create(Vec2 c, double r)
    {
        if (r > 0) Execute(new AddEntitiesCommand([Styled(new CircleEntity(Doc.CurrentLayer, c, r))], "円"));
        // 続けて次の円を描ける (Esc で終了)
        _center = null;
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        // 半径を固定しているときは、中心を指定する前からカーソル位置に円を出す
        if (_center is not Vec2 c)
        {
            if (FixedRadius is double fr)
                dc.DrawEllipse(null, CadCanvas.PreviewDashPen, Canvas.WorldToScreen(_cursor), fr * Canvas.Scale, fr * Canvas.Scale);
            return;
        }
        var r = c.DistanceTo(_cursor) * Canvas.Scale;
        dc.DrawEllipse(null, CadCanvas.PreviewPen, Canvas.WorldToScreen(c), r, r);
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, c, _cursor);
    }
}

/// <summary>
/// 円弧。通常は 3 点 (始点 → 通過点 → 終点)。
/// 半径を入れているときは 始点 → 終点 → 膨らむ側 (円弧の近くをクリック) で、その半径の円弧になる。
/// </summary>
public sealed class ArcTool : CadTool, IStyledDrawTool, IRadiusInput
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;

    public override string Name => "円弧";
    public override Vec2? BasePoint => _points.Count > 0 ? _points[^1] : null;

    public double? FixedRadius
    {
        get => DrawInputMemory.ArcRadius;
        set
        {
            DrawInputMemory.ArcRadius = value;
            // 途中で切り替えたら最初から
            _points.Clear();
            if (Canvas != null) OnActivated();
        }
    }

    /// <summary>今の半径の目安 (3点モードで 2 点目まで決まっているとき)。</summary>
    public double? LiveRadius =>
        FixedRadius == null && _points.Count == 2 && GeoMath.CircleFrom3Points(_points[0], _points[1], _cursor) is var (_, r) ? r : null;

    protected override void OnActivated() =>
        SetPrompt(FixedRadius is double r ? $"円弧の始点を指定 (半径 {r:0.###} m)" : "円弧の始点を指定");

    public override bool CanStepBack => _points.Count > 0;

    /// <summary>最後に指定した点を取り消し、その点を指定する手順からやり直す。</summary>
    public override void StepBack()
    {
        if (_points.Count == 0) return;
        _points.RemoveAt(_points.Count - 1);
        if (_points.Count == 0)
        {
            OnActivated();
        }
        else if (FixedRadius is double radius)
        {
            SetPrompt($"終点を指定 (半径 {radius:0.###} m、2 点間は {radius * 2:0.###} m まで)");
        }
        else
        {
            SetPrompt(_points.Count == 1 ? "通過点を指定" : "終点を指定");
        }
    }

    public override void OnPoint(Vec2 p)
    {
        if (FixedRadius is double radius)
        {
            OnPointWithRadius(p, radius);
            return;
        }
        _points.Add(p);
        if (_points.Count == 1) SetPrompt("通過点を指定");
        else if (_points.Count == 2) SetPrompt("終点を指定");
        else
        {
            if (ArcEntity.From3Points(Doc.CurrentLayer, _points[0], _points[1], _points[2]) is { } arc)
                Execute(new AddEntitiesCommand([Styled(arc)], "円弧"));
            else
                Canvas.ShowMessage("3点が一直線上にあるため円弧を作れません。");
            _points.Clear(); // 続けて次の円弧を描ける (Esc で終了)
            OnActivated();
        }
    }

    private void OnPointWithRadius(Vec2 p, double radius)
    {
        switch (_points.Count)
        {
            case 0:
                _points.Add(p);
                SetPrompt($"終点を指定 (半径 {radius:0.###} m、2 点間は {radius * 2:0.###} m まで)");
                break;
            case 1:
                if (p.DistanceTo(_points[0]) > radius * 2 + 1e-9)
                {
                    Canvas.ShowMessage($"2 点間 ({p.DistanceTo(_points[0]):0.###} m) が直径 ({radius * 2:0.###} m) より長いため、半径 {radius:0.###} m の円弧は描けません。");
                    return;
                }
                if (p.DistanceTo(_points[0]) < 1e-9) return;
                _points.Add(p);
                SetPrompt("膨らむ側を指定 (表示されている円弧の近くをクリック。反対側・大きい側にも切り替わります)");
                break;
            default:
                if (ArcEntity.FromChordAndRadius(Doc.CurrentLayer, _points[0], _points[1], radius, p) is { } arc)
                    Execute(new AddEntitiesCommand([Styled(arc)], "円弧"));
                _points.Clear(); // 続けて次の円弧を描ける (Esc で終了)
                OnActivated();
                break;
        }
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_points.Count == 1) Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, _points[0], _cursor);
        if (_points.Count != 2) return;
        var arc = FixedRadius is double r
            ? ArcEntity.FromChordAndRadius(Doc.CurrentLayer, _points[0], _points[1], r, _cursor)
            : ArcEntity.From3Points(Doc.CurrentLayer, _points[0], _points[1], _cursor);
        if (arc != null) Canvas.DrawEntity(dc, arc, PreviewColor);
    }
}

public sealed class TextTool : CadTool, IStyledDrawTool
{
    /// <summary>
    /// false (既定) なら用紙上の mm (<see cref="DefaultHeightMm"/>) を図面の縮尺で m に換算する (1:500 で 3.5mm → 1.75m)。
    /// 拡大・縮小すると線と一緒に大きく・小さくなる。
    /// true なら、新しい文字は画面上で一定の大きさ (<see cref="DefaultScreenPx"/>) にする (拡大率によらず同じ大きさで見える)。
    /// </summary>
    public static bool ScreenFixed { get; set; }

    /// <summary>画面で一定の文字の高さ (ピクセル)。</summary>
    public static double DefaultScreenPx { get; set; } = 16;

    /// <summary>文字高 (用紙上の mm)。<see cref="ScreenFixed"/> が false のときに使う。</summary>
    public static double DefaultHeightMm { get; set; } = 3.5;

    private Vec2? _position;

    public override string Name => "文字";
    public override bool WantsTextInput => _position != null;
    public override bool CanStepBack => _position != null;

    public override void StepBack()
    {
        _position = null;
        OnActivated();
    }
    protected override void OnActivated() =>
        SetPrompt(ScreenFixed
            ? $"文字の位置 (左下) を指定 [大きさ: 画面で {DefaultScreenPx:0.#}px (縮尺によらず一定) / H=数値(px) で変更]"
            : $"文字の位置 (左下) を指定 [文字高 {DefaultHeightMm:0.##}mm = {Doc.PaperMmToMeters(DefaultHeightMm):0.###}m ({Doc.ScaleText}) / H=数値(mm) で変更]");

    public override void OnPoint(Vec2 p)
    {
        _position = p;
        SetPrompt("文字を入力して Enter");
    }

    public override bool OnText(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("H=", StringComparison.OrdinalIgnoreCase) && TryParseNumber(t[2..], out var h) && h > 0)
        {
            if (ScreenFixed) DefaultScreenPx = h;
            else DefaultHeightMm = h;
            if (_position == null) OnActivated();
            return true;
        }
        if (_position is not Vec2 pos) return false;
        if (text.Length > 0) Execute(new AddEntitiesCommand([Styled(Create(Doc.CurrentLayer, pos, text, Canvas.Scale))], "文字"));
        // 続けて次の文字を置ける (Esc で終了)
        _position = null;
        OnActivated();
        return true;
    }

    /// <summary>今の設定の大きさの文字を作る (pixelsPerMeter は画面の拡大率)。</summary>
    public TextEntity Create(Layer layer, Vec2 pos, string text, double pixelsPerMeter) => ScreenFixed
        ? new TextEntity(layer, pos, text, DefaultScreenPx / pixelsPerMeter) { ScreenHeightPx = DefaultScreenPx }
        : new TextEntity(layer, pos, text, Doc.PaperMmToMeters(DefaultHeightMm));
}

/// <summary>
/// 点。クリック、または座標の入力 (x,y / @dx,dy / sX,Y の測量座標) で置く。続けて置ける (Esc で終了)。
/// 「一つ戻す」で直前に置いた点を取り消す。
/// </summary>
public sealed class PointTool : CadTool, IStyledDrawTool
{
    private readonly List<(IUndoableCommand Command, PointEntity Point)> _placed = [];

    public override string Name => "点";

    /// <summary>相対座標 (@) は直前に置いた点から。</summary>
    public override Vec2? BasePoint => _placed.Count > 0 ? _placed[^1].Point.Position : null;

    public override bool CanStepBack => _placed.Count > 0;

    protected override void OnActivated() =>
        SetPrompt("点の位置を指定 (座標の入力も可: x,y / @dx,dy / s X,Y は測量座標)" + (_placed.Count > 0 ? $"  {_placed.Count} 点を配置" : ""));

    public override void OnPoint(Vec2 p)
    {
        var point = Styled(new PointEntity(Doc.CurrentLayer, p));
        var command = new AddEntitiesCommand([point], "点");
        Execute(command);
        _placed.Add((command, point));
        OnActivated();
    }

    public override void StepBack()
    {
        if (_placed.Count == 0) return;
        var (command, point) = _placed[^1];
        _placed.RemoveAt(_placed.Count - 1);
        Retract(command, [point]);
        OnActivated();
    }
}

/// <summary>
/// 矩形 (REC): 1 つ目の角 → 対角の角 を指定すると、閉じたポリライン (四角) を描く。
/// 対角は「@幅,高さ」(1 つ目の角から) でも入力できる。続けて描ける (Esc で終了)。
/// </summary>
public sealed class RectangleTool : CadTool, IStyledDrawTool
{
    private Vec2? _first;
    private Vec2 _cursor;

    public override string Name => "矩形";
    public override Vec2? BasePoint => _first;
    public override bool CanStepBack => _first != null;

    protected override void OnActivated() =>
        SetPrompt(_first == null ? "矩形の 1 つ目の角を指定" : "対角の角を指定 (「@幅,高さ」で大きさを入力も可。例: @10,5)");

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
            OnActivated();
            return;
        }
        if (Math.Abs(p.X - a.X) < 1e-9 || Math.Abs(p.Y - a.Y) < 1e-9)
        {
            Canvas.ShowMessage("矩形: 幅と高さが 0 にならないよう、対角の角を指定してください。");
            return;
        }
        Execute(new AddEntitiesCommand([Styled(new PolylineEntity(Doc.CurrentLayer, Corners(a, p), true))], "矩形"));
        Canvas.ShowMessage($"矩形を描きました (幅 {Math.Abs(p.X - a.X):0.###} m × 高さ {Math.Abs(p.Y - a.Y):0.###} m)。");
        _first = null;
        OnActivated();
    }

    /// <summary>対角の 2 点から四隅 (反時計回り、1 つ目の角から)。</summary>
    public static Vec2[] Corners(Vec2 a, Vec2 b)
    {
        Vec2[] c = [a, new Vec2(b.X, a.Y), b, new Vec2(a.X, b.Y)];
        // 反時計回りにそろえる (面積が正)
        var area = (c[1] - c[0]).Cross(c[2] - c[0]);
        return area >= 0 ? c : [c[0], c[3], c[2], c[1]];
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_first is not Vec2 a) return;
        Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, Corners(a, _cursor), true);
    }
}
