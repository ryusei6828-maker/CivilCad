using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

public enum HatchStyle { Single, Cross, Solid }

/// <summary>ハッチングの設定 (作図入力バーで変更。次のハッチングでも使う)。</summary>
public static class HatchSettings
{
    /// <summary>線の角度 (度, 東 = 0, 反時計回り)。</summary>
    public static double AngleDeg { get; set; } = 45;

    /// <summary>線の間隔 (用紙上の mm)。図面の縮尺で m に換算する。</summary>
    public static double SpacingMm { get; set; } = 3;

    public static HatchStyle Style { get; set; } = HatchStyle.Single;

    /// <summary>透明度 (%)。0 = 不透明。</summary>
    public static double TransparencyPercent
    {
        get => _transparency;
        set => _transparency = Math.Clamp(value, 0, 100);
    }

    private static double _transparency;

    public static string StyleName(HatchStyle s) => s switch { HatchStyle.Cross => "クロス", HatchStyle.Solid => "塗りつぶし", _ => "斜線" };

    public static List<HatchPattern> Patterns(CadDocument doc)
    {
        var angle = AngleDeg * Math.PI / 180;
        var spacing = doc.PaperMmToMeters(SpacingMm);
        return Style switch
        {
            HatchStyle.Solid => [],
            HatchStyle.Cross => [new(angle, spacing, Vec2.Zero), new(angle + Math.PI / 2, spacing, Vec2.Zero)],
            _ => [new(angle, spacing, Vec2.Zero)],
        };
    }
}

/// <summary>
/// ハッチング。閉じた図形 (閉じたポリライン・円) の内側をクリックするとその範囲にハッチングを作る
/// (内側にある閉じた図形は中抜き)。閉じた図形が無い所では、クリックした点を頂点とする範囲を作り Enter で確定。
/// Esc で終了するまで続けて作れる。
/// fill = true なら「塗りつぶし」コマンド: 線ではなく色で塗る。開始時に閉じた図形を選択していれば、それらをすぐ塗る。
/// </summary>
public sealed class HatchTool(bool fill = false) : CadTool, IStyledDrawTool
{
    private static readonly RgbColor PreviewColor = new(0x66, 0xCC, 0xFF);
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;
    private List<List<Vec2>>? _hoverLoops;
    private bool _selectionHandled;

    public override string Name => fill ? "塗りつぶし" : "ハッチング";
    public override Vec2? BasePoint => _points.Count > 0 ? _points[^1] : null;

    /// <summary>塗りつぶしコマンドとして動いている。</summary>
    public bool IsFill => fill;

    private bool Solid => fill || HatchSettings.Style == HatchStyle.Solid;

    protected override void OnActivated()
    {
        if (fill && !_selectionHandled && Canvas.Selection.Count > 0)
        {
            _selectionHandled = true;
            FillSelection();
        }
        SetPrompt(_points.Count > 0
            ? "次の点を指定 (Enter で確定、Esc で取り消し / 右クリックで一つ戻す・やり直し)"
            : fill
                ? $"塗る範囲 (閉じた図形) の内側をクリック、または範囲の 1 点目を指定 [透明度 {HatchSettings.TransparencyPercent:0.#}%]"
                : $"閉じた図形の内側をクリック、または範囲の 1 点目を指定 [{HatchSettings.StyleName(HatchSettings.Style)} {HatchSettings.AngleDeg:0.##}° 間隔 {HatchSettings.SpacingMm:0.##}mm]");
    }

    /// <summary>選択中の閉じた図形を塗る (内側にある選択図形は中抜き)。</summary>
    private void FillSelection()
    {
        var selected = Canvas.Selection.ToList();
        var loops = selected.Select(ClosedLoop).Where(l => l != null).Select(l => l!).ToList();
        if (loops.Count == 0)
        {
            Canvas.ShowMessage("選択中の図形に閉じた図形がありません。結合 (J) で 1 つの閉じた線にしてから塗るか、範囲の内側をクリックしてください。");
            return;
        }
        var fills = GroupLoops(loops).Select(MakeHatch).ToList();
        Execute(new AddEntitiesCommand(fills, $"塗りつぶし ({fills.Count} 個)"));
        Canvas.ClearSelection();
        var skipped = selected.Count - loops.Count;
        Canvas.ShowMessage($"{fills.Count} 個の範囲を塗りつぶしました。" + (skipped > 0 ? $" (閉じていない図形 {skipped} 個は対象外)" : ""));
    }

    /// <summary>
    /// 閉じた点列を「外形 + 中抜き」の組に分ける。他のループの内側に偶数回入っているものが外形、
    /// 奇数回のものは、それを囲む最も小さい外形の中抜き。
    /// </summary>
    public static List<List<List<Vec2>>> GroupLoops(List<List<Vec2>> loops)
    {
        int Depth(List<Vec2> l) => loops.Count(o => o != l && HatchEntity.IsInside(o, l[0]));
        var outers = loops.Where(l => Depth(l) % 2 == 0).ToList();
        var groups = outers.ToDictionary(o => o, o => new List<List<Vec2>> { o });
        foreach (var hole in loops.Where(l => Depth(l) % 2 == 1))
        {
            var owner = outers.Where(o => HatchEntity.IsInside(o, hole[0])).MinBy(o => Math.Abs(HatchEntity.SignedArea(o)));
            if (owner != null) groups[owner].Add(hole);
        }
        return [.. groups.Values];
    }

    private HatchEntity MakeHatch(List<List<Vec2>> loops) =>
        Styled(new HatchEntity(Doc.CurrentLayer, loops, Solid ? [] : HatchSettings.Patterns(Doc), Solid)
        {
            TransparencyPercent = HatchSettings.TransparencyPercent,
        });

    public override void OnPoint(Vec2 p)
    {
        if (_points.Count == 0 && FindRegion(Doc, p) is { } loops)
        {
            Create(loops);
            _hoverLoops = null;
            return;
        }
        if (_points.Count == 0 || _points[^1].DistanceTo(p) > 1e-9) _points.Add(p);
        OnActivated();
    }

    public override void OnMouseMove(Vec2 p)
    {
        _cursor = p;
        _hoverLoops = _points.Count == 0 ? FindRegion(Doc, p) : null;
    }

    public override void OnEnter()
    {
        if (_points.Count >= 3) Create([[.. _points]]);
        else if (_points.Count > 0) Canvas.ShowMessage("範囲は 3 点以上で指定してください。");
        _points.Clear();
        OnActivated();
    }

    public override bool CanStepBack => _points.Count > 0;
    public override string? ConfirmLabel => _points.Count >= 3 ? "範囲を確定 (Enter)" : null;

    public override void StepBack()
    {
        if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
        OnActivated();
    }

    public override void OnCancel()
    {
        if (_points.Count > 0)
        {
            _points.Clear();
            OnActivated();
        }
        else
        {
            Finish();
        }
    }

    private void Create(List<List<Vec2>> loops)
    {
        var hatch = MakeHatch(loops);
        if (!hatch.Solid && hatch.Lines.Count == 0)
        {
            Canvas.ShowMessage("間隔に対して範囲が小さすぎる (または大きすぎる) ため、ハッチ線ができません。間隔を見直してください。");
            return;
        }
        Execute(new AddEntitiesCommand([hatch], Name));
    }

    public override void DrawPreview(DrawingContext dc)
    {
        List<List<Vec2>>? loops = null;
        if (_points.Count > 0)
        {
            Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, [.. _points, _cursor], _points.Count >= 2);
            if (_points.Count >= 2) loops = [[.. _points, _cursor]];
        }
        else if (_hoverLoops != null)
        {
            loops = _hoverLoops;
            foreach (var l in loops) Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewDashPen, l, true);
        }
        if (loops == null) return;
        var preview = new HatchEntity(Doc.CurrentLayer, loops, Solid ? [] : HatchSettings.Patterns(Doc), Solid)
        {
            TransparencyPercent = Math.Max(HatchSettings.TransparencyPercent, Solid ? 50 : 0), // 塗りの下絵が見えるよう半透明で
        };
        if (preview.Solid || preview.Lines.Count <= 5000) Canvas.DrawEntity(dc, preview, PreviewColor);
    }

    // ---- 範囲の検出 ----

    /// <summary>
    /// 点を囲む閉じた図形のうち最も小さいものを外形にし、その内側にある閉じた図形 (いちばん外側のもの) を中抜きにする。
    /// 見つからなければ null。
    /// </summary>
    public static List<List<Vec2>>? FindRegion(CadDocument doc, Vec2 p)
    {
        var best = doc.Entities
            .Where(e => e is PolylineEntity or CircleEntity && doc.IsVisible(e) && e.Bounds.Contains(p))
            .Select(e => (Entity: e, Loop: ClosedLoop(e)))
            .Where(x => x.Loop != null && HatchEntity.IsInside(x.Loop, p))
            .OrderBy(x => Math.Abs(HatchEntity.SignedArea(x.Loop!)))
            .FirstOrDefault();
        if (best.Loop is not { } outer) return null;

        var outerArea = Math.Abs(HatchEntity.SignedArea(outer));
        var outerBox = best.Entity.Bounds;
        var inside = doc.Entities
            .Where(e => e != best.Entity && e is PolylineEntity or CircleEntity && doc.IsVisible(e) && outerBox.Contains(e.Bounds))
            .Select(ClosedLoop).Where(l => l != null).Select(l => l!)
            .Where(l => Math.Abs(HatchEntity.SignedArea(l)) < outerArea && l.All(q => HatchEntity.IsInside(outer, q)) && !HatchEntity.IsInside(l, p))
            .ToList();
        // 中抜きの中にさらにある図形は除く (いちばん外側の中抜きだけ)
        var holes = inside.Where(h => !inside.Any(o => o != h && Math.Abs(HatchEntity.SignedArea(o)) > Math.Abs(HatchEntity.SignedArea(h)) && h.All(q => HatchEntity.IsInside(o, q)))).ToList();
        return [outer, .. holes];
    }

    /// <summary>閉じた図形なら境界の点列 (閉じたポリライン・始点に戻るポリライン・円)。</summary>
    public static List<Vec2>? ClosedLoop(Entity e)
    {
        switch (e)
        {
            case PolylineEntity pl when pl.Points.Count >= 3:
                var closed = pl.IsClosed || pl.Filled || pl.Points[0].DistanceTo(pl.Points[^1]) < 1e-9;
                if (!closed) return null;
                var pts = pl.Points.ToList();
                if (pts[0].DistanceTo(pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
                return pts.Count >= 3 ? pts : null;
            case CircleEntity c:
                const int n = 72;
                return [.. Enumerable.Range(0, n).Select(k => c.Center + Vec2.FromAngle(k * 2 * Math.PI / n) * c.Radius)];
            default:
                return null;
        }
    }
}
