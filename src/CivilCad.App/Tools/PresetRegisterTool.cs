using System.Windows.Media;
using CivilCad.App.Views;
using CivilCad.Core.Commands;
using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.App.Tools;

/// <summary>プリセット登録で入力する名前と、選択した図形を記号に置き換えるか。</summary>
public sealed record PresetRegisterInput(string Name, bool ReplaceSelection);

/// <summary>
/// 選択した図形をプリセットに登録する。基準点 (置くときにカーソルが付く点) を指定し、名前を入力する。
/// 登録した大きさ (用紙上の mm) で置くと、元の図形と同じ大きさになる。
/// </summary>
public sealed class PresetRegisterTool(Func<string, PresetRegisterInput?> askName) : CadTool
{
    private List<Entity> _source = [];

    public override string Name => "プリセット登録";
    public override bool RequiresSelection => true;

    protected override void OnActivated()
    {
        _source = [.. Doc.Entities.Where(Canvas.Selection.Contains)];
        SetPrompt($"選択した {_source.Count} 個の図形をプリセットにします。基準点 (置くときの位置) を指定 (Enter: 選択範囲の中心)");
    }

    public override void OnEnter()
    {
        var box = _source.Aggregate(Box2.Empty, (acc, e) => acc.Union(e.Bounds));
        if (box.IsEmpty) Finish();
        else Register(box.Center);
    }

    public override void OnPoint(Vec2 p) => Register(p);

    private void Register(Vec2 basePoint)
    {
        if (_source.Count == 0)
        {
            Finish();
            return;
        }
        var defaultName = $"登録プリセット {SymbolLibrary.User.Count + 1}";
        if (askName(defaultName) is not { } input || string.IsNullOrWhiteSpace(input.Name))
        {
            Canvas.ShowMessage("プリセット登録を取り消しました。");
            Finish();
            return;
        }

        var def = SymbolLibrary.FromEntities(input.Name.Trim(), _source, basePoint, Doc.PaperMmToMeters(1), out var skipped);
        if (def.Shapes.Count == 0)
        {
            Canvas.ShowMessage("プリセット登録: 記号にできる図形がありません (画像は登録できません)。");
            Finish();
            return;
        }
        SymbolLibrary.AddUser(def);
        Settings.UserPresetStore.Save();
        PresetSettings.Kind = def.Kind;
        PresetSettings.SizeMm = def.DefaultSizeMm;
        PresetSettings.AngleDeg = 0;

        if (input.ReplaceSelection)
        {
            var symbol = new SymbolEntity(_source[0].Layer, def.Kind, basePoint, def.DefaultSizeMm, Doc.PaperMmToMeters(def.DefaultSizeMm));
            Execute(new CompositeCommand($"プリセット「{def.Name}」に置き換え",
                new RemoveEntitiesCommand(_source), new AddEntitiesCommand([symbol])));
            Canvas.SetSelection([symbol]);
        }
        Canvas.ShowMessage($"プリセット「{def.Name}」を登録しました (大きさ {def.DefaultSizeMm:0.##}mm、{def.Shapes.Count} 個の図形)。" +
                           "プリセット (PS) の種類から選んで置けます。" + (skipped > 0 ? $" (画像など {skipped} 個は記号にできないため除きました)" : ""));
        Finish();
    }

    public override void DrawPreview(DrawingContext dc)
    {
        foreach (var e in _source) Canvas.DrawEntity(dc, e, new RgbColor(0x66, 0xCC, 0xFF), true);
    }
}
