using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>方位記号の向きの切り替え (手動 / 自動) 。</summary>
public static class NorthArrows
{
    public static bool IsNorthArrow(Entity e) => e is SymbolEntity s && s.Definition.PointsNorth;

    /// <summary>
    /// 自動セット: 方位記号を北に合わせる (自動に戻す)。選択中の方位記号、選択が無ければ図面の手動の方位記号すべて。
    /// 戻り値は合わせた数。
    /// </summary>
    public static int SetAuto(CadDocument doc, IEnumerable<Entity> selection)
    {
        var targets = selection.Where(IsNorthArrow).Cast<SymbolEntity>().ToList();
        if (targets.Count == 0) targets = [.. doc.Entities.OfType<SymbolEntity>().Where(s => s.Definition.PointsNorth && !s.AutoNorth)];
        targets = [.. targets.Where(s => doc.Entities.Contains(s))];
        if (targets.Count == 0) return 0;
        doc.History.Execute(new ReplaceEntitiesCommand(targets.Select(s =>
        {
            var c = (SymbolEntity)s.Clone();
            c.AutoNorth = true;
            return ((Entity)s, (Entity)c);
        }), "方位記号を北に合わせる"));
        return targets.Count;
    }
}

/// <summary>
/// 手動向き: 選択した方位記号の向きを手で決める (N を向ける方向をクリック、または角度を入力)。
/// 手動にした記号は、移動・回転してもそのまま (北に合わせ直さない)。自動セット (NA) で北に戻せる。
/// </summary>
public sealed class NorthManualTool : CadTool
{
    private List<SymbolEntity> _targets = [];
    private Vec2 _cursor;

    public override string Name => "方位記号の向き (手動)";
    public override bool RequiresSelection => true;
    public override bool WantsTextInput => true;
    public override Vec2? BasePoint => _targets.Count > 0 ? _targets[0].Position : null;

    protected override void OnActivated()
    {
        _targets = [.. Doc.Entities.Where(Canvas.Selection.Contains).Where(NorthArrows.IsNorthArrow).Cast<SymbolEntity>()];
        if (_targets.Count == 0)
        {
            Canvas.ShowMessage("方位記号の向き (手動): 方位記号を選択してから実行してください。");
            Finish();
            return;
        }
        SetPrompt($"方位記号 {_targets.Count} 個: N を向ける方向をクリック、または角度 (°, 図面の上 = 0、反時計回り) を入力");
    }

    public override void OnMouseMove(Vec2 p) => _cursor = p;

    public override void OnPoint(Vec2 p)
    {
        if (BasePoint is not Vec2 b || (p - b).Length < 1e-9) return;
        Apply((p - b).Angle - Math.PI / 2);
    }

    public override bool OnText(string text)
    {
        if (!TryParseNumber(text, out var deg)) return false;
        Apply(deg * Math.PI / 180);
        return true;
    }

    private void Apply(double rotation)
    {
        Execute(new ReplaceEntitiesCommand(_targets.Select(s =>
        {
            var c = (SymbolEntity)s.Clone();
            c.AutoNorth = false;
            c.Rotation = rotation;
            return ((Entity)s, (Entity)c);
        }), "方位記号の向き (手動)"));
        Canvas.ShowMessage($"方位記号 {_targets.Count} 個を {rotation * 180 / Math.PI:0.###}° (図面の上から反時計回り) に向けました (手動)。北に戻すには 自動セット (NA)。");
        Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        if (BasePoint is not Vec2 b || (_cursor - b).Length < 1e-9) return;
        dc.DrawLine(CadCanvas.PreviewDashPen, Canvas.WorldToScreen(b), Canvas.WorldToScreen(_cursor));
        var rotation = (_cursor - b).Angle - Math.PI / 2;
        foreach (var s in _targets)
        {
            var preview = (SymbolEntity)s.Clone();
            preview.Rotation = rotation;
            Canvas.DrawEntity(dc, preview, new RgbColor(0x66, 0xCC, 0xFF));
        }
    }
}
