using CivilCad.Core.Geometry;

namespace CivilCad.App.Tools;

/// <summary>
/// 移動基準点 (CivilCad3D に載せたときだけ使う): クリックした点 (スナップ可) を渡して終わる。Esc でやめる。
/// 「スケッチへ」で送ったあと、スケッチで置くときにカーソルに付く点を決める。
/// </summary>
public sealed class MoveReferenceTool(Action<Vec2> picked) : CadTool
{
    public override string Name => "移動基準点";

    protected override void OnActivated() => SetPrompt("移動基準点をクリック (Esc でやめる)");

    public override void OnPoint(Vec2 p)
    {
        picked(p);
        Finish();
    }
}
