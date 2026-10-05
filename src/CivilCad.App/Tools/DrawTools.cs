using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

public sealed class LineTool : CadTool, ILengthAngleInput, IStyledDrawTool
{
    private Vec2? _last;
    private Vec2 _cursor;

    public override string Name => "線";
    public override Vec2? BasePoint => _last;
    public double? FixedLength { get => DrawInputMemory.Length; set => DrawInputMemory.Length = value; }
    public double? FixedAngleDeg { get => DrawInputMemory.AngleDeg; set => DrawInputMemory.AngleDeg = value; }
    public (double Length, double AngleDeg)? Live => _last is Vec2 b ? LengthAngle.Measure(b, _cursor) : null;

    protected override void OnActivated() => SetPrompt("始点を指定");

    public override Vec2 ConstrainCursor(Vec2 p) =>
        _last is Vec2 b ? LengthAngle.Apply(b, p, FixedLength, FixedAngleDeg) : p;

    public override void OnPoint(Vec2 p)
    {
        if (_last is Vec2 last && last != p)
            Execute(new AddEntitiesCommand([Styled(new LineEntity(Doc.CurrentLayer, last, p))], "線"));
        _last = p;
        SetPrompt("次の点を指定 (Enter / 右クリックで終了)");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_last is Vec2 last) Canvas.DrawWorldLine(dc, CadCanvas.PreviewPen, last, _cursor);
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

    protected override void OnActivated() => SetPrompt("始点を指定");

    public override Vec2 ConstrainCursor(Vec2 p) =>
        BasePoint is Vec2 b ? LengthAngle.Apply(b, p, FixedLength, FixedAngleDeg) : p;

    public override void OnPoint(Vec2 p)
    {
        if (_points.Count > 0 && _points[^1] == p) return;
        _points.Add(p);
        SetPrompt("次の点を指定 (C = 閉じる / Enter で確定)");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override bool OnText(string text)
    {
        if (!text.Trim().Equals("C", StringComparison.OrdinalIgnoreCase)) return false;
        Commit(true);
        return true;
    }

    public override void OnEnter() => Commit(false);

    private void Commit(bool closed)
    {
        if (_points.Count >= 2)
            Execute(new AddEntitiesCommand([Styled(new PolylineEntity(Doc.CurrentLayer, _points, closed && _points.Count > 2))], "ポリライン"));
        Finish();
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
        Finish();
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
            Finish();
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
                Finish();
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
    /// <summary>文字高 (用紙上の mm)。図面の縮尺で m に換算して作図する (1:500 で 3.5mm → 1.75m)。</summary>
    public static double DefaultHeightMm { get; set; } = 3.5;

    private Vec2? _position;

    public override string Name => "文字";
    public override bool WantsTextInput => _position != null;
    protected override void OnActivated() =>
        SetPrompt($"文字の位置 (左下) を指定 [文字高 {DefaultHeightMm:0.##}mm = {Doc.PaperMmToMeters(DefaultHeightMm):0.###}m ({Doc.ScaleText}) / H=数値(mm) で変更]");

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
            DefaultHeightMm = h;
            if (_position == null) OnActivated();
            return true;
        }
        if (_position is not Vec2 pos) return false;
        if (text.Length > 0)
            Execute(new AddEntitiesCommand([Styled(new TextEntity(Doc.CurrentLayer, pos, text, Doc.PaperMmToMeters(DefaultHeightMm)))], "文字"));
        Finish();
        return true;
    }
}
