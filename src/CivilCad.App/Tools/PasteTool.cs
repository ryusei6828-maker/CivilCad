using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>
/// 位置を指定して貼り付け (PA): コピーした図形の左下を基点に、クリックした位置へ貼り付ける。
/// 続けてクリックすると、同じ図形をもう一度貼り付ける (Esc / Enter で終了。最後に貼り付けた図形が選択される)。
/// </summary>
public sealed class PasteTool : CadTool
{
    private const int MaxPreviewEntities = 300;
    private List<Entity> _items = [];
    private Vec2 _base;
    private Vec2 _cursor;
    private List<Entity> _last = [];

    public override string Name => "貼り付け";
    public override Vec2? BasePoint => null;
    public override string? ConfirmLabel => _last.Count > 0 ? "貼り付けを終える (Enter)" : null;

    protected override void OnActivated()
    {
        _items = EntityClipboard.CreateFor(Doc);
        _base = _items.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds)).Min;
        _cursor = _base;
        SetPrompt(_items.Count == 0 ? "コピーした図形がありません (先に Ctrl+C でコピー)" : $"貼り付ける位置 (左下) を指定: {_items.Count} 個");
    }

    public override void OnPoint(Vec2 p)
    {
        if (_items.Count == 0) return;
        var delta = p - _base;
        var pasted = _items.Select(e => e.Clone()).ToList();
        pasted.ForEach(e => e.Translate(delta));
        Execute(new AddEntitiesCommand(pasted, "貼り付け"));
        _last = pasted;
        SetPrompt($"{pasted.Count} 個を貼り付けました。続けて位置を指定 (Enter / Esc で終了)");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void OnEnter() => Done();
    public override void OnCancel() => Done();

    private void Done()
    {
        var last = _last;
        Finish();
        if (last.Count > 0) Canvas.SetSelection(last);
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (_items.Count == 0) return;
        var delta = _cursor - _base;
        if (_items.Count > MaxPreviewEntities)
        {
            var box = _items.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
            Canvas.DrawWorldPolyline(dc, CadCanvas.PreviewPen,
                [box.Min + delta, new Vec2(box.Max.X, box.Min.Y) + delta, box.Max + delta, new Vec2(box.Min.X, box.Max.Y) + delta], true);
            return;
        }
        foreach (var e in _items)
        {
            var ghost = e.Clone();
            ghost.Translate(delta);
            Canvas.DrawEntity(dc, ghost, new RgbColor(0x66, 0xCC, 0xFF));
        }
    }
}
