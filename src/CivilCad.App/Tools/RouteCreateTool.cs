using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>
/// 路線作成 (RL): 図面上で BP → IP → … → EP の順にクリック (スナップ・座標の入力も可) し、Enter で確定する。
/// 確定すると done にクリックした点 (図面座標) を渡す (路線計算の窓を IP 表入りで開き、R・A・幅を入れて作図する)。
/// </summary>
public sealed class RouteCreateTool(Action<List<Vec2>> done) : CadTool
{
    private readonly List<Vec2> _points = [];
    private Vec2 _cursor;

    public override string Name => "路線作成";
    public override Vec2? BasePoint => _points.Count > 0 ? _points[^1] : null;
    public override bool CanStepBack => _points.Count > 0;
    public override string? ConfirmLabel => _points.Count >= 2 ? "確定 (路線計算の窓で R・A・幅を入れる)" : null;

    /// <summary>クリックした点 (確認用)。</summary>
    public IReadOnlyList<Vec2> Points => _points;

    protected override void OnActivated() => SetPrompt(_points.Count switch
    {
        0 => "路線の起点 (BP) を指定",
        1 => "IP1 (曲がる点) または終点を指定",
        _ => $"IP{_points.Count} または終点を指定 / Enter で確定 ({_points.Count} 点。最後の点が終点 EP)",
    });

    public override void OnPoint(Vec2 p)
    {
        if (_points.Count > 0 && p.DistanceTo(_points[^1]) < 1e-6) return; // 同じ点は続けて置かない
        _points.Add(p);
        OnActivated();
    }

    public override void StepBack()
    {
        if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
        OnActivated();
    }

    public override void OnEnter()
    {
        if (_points.Count < 2)
        {
            if (_points.Count == 1) Canvas.ShowMessage("路線作成: 起点のほかに少なくとも 1 点 (終点) を指定してください。");
            else Finish();
            return;
        }
        var pts = _points.ToList();
        Finish();
        done(pts);
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (_points.Count == 0) return;
        if (_points.Count >= 2) Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen, _points, false);
        Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, _points[^1], _cursor);
    }
}
