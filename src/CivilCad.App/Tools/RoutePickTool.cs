using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>
/// 路線パネルの「図面から追加」「選んだ行を図面で指定」: 図面でクリックした点を路線パネルの表に入れる。
/// single なら 1 点で終わる。そうでなければ Enter / Esc まで続けて点を追加する (最後の点が終点)。
/// 前の点 (表の最後の点など) からカーソルまで線を出して、IP 線の見当がつくようにする。
/// </summary>
public sealed class RoutePickTool(Action<Vec2> picked, Action finished, Func<Vec2?> lastPoint, bool single) : CadTool
{
    private Vec2 _cursor;

    public override string Name => single ? "路線の点の指定" : "路線の点の追加";
    public override Vec2? BasePoint => lastPoint();
    public override string? ConfirmLabel => single ? null : "点の追加を終える (Enter)";

    protected override void OnActivated() => SetPrompt(single
        ? "選んだ行の点を図面でクリック (Esc でやめる)"
        : "路線の点 (始点 → IP → … → 終点) を順にクリック / Enter で終わる (最後の点が終点)");

    public override void OnPoint(Vec2 p)
    {
        picked(p);
        if (single) End();
    }

    public override void OnEnter() => End();

    public override void OnCancel() => End();

    private void End()
    {
        finished();
        Finish();
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void DrawPreview(DrawingContext dc)
    {
        if (lastPoint() is Vec2 b && !single) Canvas.DrawWorldLine(dc, CadCanvas.PreviewDashPen, b, _cursor);
    }
}
