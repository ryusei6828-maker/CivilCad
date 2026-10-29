using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Commands;

public sealed class AddEntitiesCommand(IEnumerable<Entity> entities, string name = "追加") : IUndoableCommand
{
    private readonly List<Entity> _entities = [.. entities];
    public string Name => name;

    public void Do(CadDocument doc)
    {
        // グループ編集中に描いた・複写した図形は、そのグループに入れる
        if (doc.EditingGroupId is Guid g) _entities.ForEach(e => e.GroupId = g);
        doc.Entities.AddRange(_entities);
    }

    public void Undo(CadDocument doc)
    {
        var set = _entities.ToHashSet();
        doc.Entities.RemoveAll(set.Contains);
    }
}

public sealed class RemoveEntitiesCommand(IEnumerable<Entity> entities, string name = "削除") : IUndoableCommand
{
    private readonly HashSet<Entity> _entities = [.. entities];
    private List<(int Index, Entity Entity)> _removed = [];
    public string Name => name;

    public void Do(CadDocument doc)
    {
        // 元の描画順に戻せるよう位置を記録
        _removed = doc.Entities.Select((e, i) => (i, e)).Where(x => _entities.Contains(x.e)).ToList();
        doc.Entities.RemoveAll(_entities.Contains);
    }

    public void Undo(CadDocument doc)
    {
        foreach (var (index, entity) in _removed) doc.Entities.Insert(index, entity);
    }
}

public sealed class TranslateEntitiesCommand(IEnumerable<Entity> entities, Vec2 delta) : IUndoableCommand
{
    private readonly List<Entity> _entities = [.. entities];
    public string Name => "移動";
    public void Do(CadDocument doc) => _entities.ForEach(e => e.Translate(delta));
    public void Undo(CadDocument doc) => _entities.ForEach(e => e.Translate(-delta));
}

/// <summary>origin 中心に rad 回転してから delta 移動する (座標設定など)。</summary>
public sealed class RotateTranslateCommand(IEnumerable<Entity> entities, Vec2 origin, double rad, Vec2 delta, string name = "回転・移動") : IUndoableCommand
{
    private readonly List<Entity> _entities = [.. entities];
    public string Name => name;

    public void Do(CadDocument doc)
    {
        foreach (var e in _entities)
        {
            if (rad != 0) e.Rotate(origin, rad);
            e.Translate(delta);
        }
    }

    public void Undo(CadDocument doc)
    {
        foreach (var e in _entities)
        {
            e.Translate(-delta);
            if (rad != 0) e.Rotate(origin, -rad);
        }
    }
}

public sealed class ChangeLayerCommand(IEnumerable<Entity> entities, Layer layer) : IUndoableCommand
{
    private readonly List<(Entity Entity, Layer Old)> _items = entities.Select(e => (e, e.Layer)).ToList();
    public string Name => $"レイヤ変更 → {layer.Name}";
    public void Do(CadDocument doc) => _items.ForEach(x => x.Entity.Layer = layer);
    public void Undo(CadDocument doc) => _items.ForEach(x => x.Entity.Layer = x.Old);
}

/// <summary>
/// 図形を置き換える (プロパティ編集用)。元の図形は残しておき、Undo で同じ位置に戻す。
/// </summary>
public sealed class ReplaceEntitiesCommand(IEnumerable<(Entity Old, Entity New)> pairs, string name = "プロパティ変更") : IUndoableCommand
{
    private readonly List<(Entity Old, Entity New)> _pairs = [.. pairs];
    public string Name => name;

    public void Do(CadDocument doc) => Swap(doc, p => p.Old, p => p.New);
    public void Undo(CadDocument doc) => Swap(doc, p => p.New, p => p.Old);

    private void Swap(CadDocument doc, Func<(Entity Old, Entity New), Entity> from, Func<(Entity Old, Entity New), Entity> to)
    {
        var map = _pairs.ToDictionary(from, to);
        for (var i = 0; i < doc.Entities.Count; i++)
            if (map.TryGetValue(doc.Entities[i], out var replacement)) doc.Entities[i] = replacement;
    }
}

public sealed class SetScaleCommand(double scaleDenominator) : IUndoableCommand
{
    private double _old;
    public string Name => $"縮尺 1:{scaleDenominator:0.##}";

    public void Do(CadDocument doc)
    {
        _old = doc.ScaleDenominator;
        doc.ScaleDenominator = scaleDenominator;
        ResizeSymbols(doc);
    }

    public void Undo(CadDocument doc)
    {
        doc.ScaleDenominator = _old;
        ResizeSymbols(doc);
    }

    /// <summary>プリセット (記号) は用紙上の大きさを保つので、図面上の大きさを新しい縮尺で合わせ直す。</summary>
    private static void ResizeSymbols(CadDocument doc)
    {
        foreach (var s in doc.Entities.OfType<SymbolEntity>()) s.Size = doc.PaperMmToMeters(s.SizeMm);
    }
}

public sealed class SetPlaneZoneCommand(int? zone) : IUndoableCommand
{
    private int? _old;
    public string Name => zone is int z ? $"平面直角座標系 {z}系" : "平面直角座標系 未設定";

    public void Do(CadDocument doc)
    {
        _old = doc.PlaneZone;
        doc.PlaneZone = zone;
    }

    public void Undo(CadDocument doc) => doc.PlaneZone = _old;
}

/// <summary>グループ化 (groupId を付ける) / グループ解除 (null)。元のグループは Undo で戻す。</summary>
public sealed class SetGroupCommand(IEnumerable<Entity> entities, Guid? groupId, string name) : IUndoableCommand
{
    private readonly List<(Entity Entity, Guid? Old)> _items = entities.Select(e => (e, e.GroupId)).ToList();
    public string Name => name;
    public void Do(CadDocument doc) => _items.ForEach(x => x.Entity.GroupId = groupId);
    public void Undo(CadDocument doc) => _items.ForEach(x => x.Entity.GroupId = x.Old);
}

/// <summary>複数コマンドを 1 回の Undo 単位にまとめる。</summary>
public sealed class CompositeCommand(string name, params IUndoableCommand[] commands) : IUndoableCommand
{
    public string Name => name;

    public void Do(CadDocument doc)
    {
        foreach (var c in commands) c.Do(doc);
    }

    public void Undo(CadDocument doc)
    {
        for (var i = commands.Length - 1; i >= 0; i--) commands[i].Undo(doc);
    }
}

/// <summary>1 つの図形を 0 個以上の図形に置き換える (トリムなど)。描画順は元の位置を保つ。</summary>
public sealed class SplitEntityCommand(Entity original, IEnumerable<Entity> pieces, string name) : IUndoableCommand
{
    private readonly List<Entity> _pieces = [.. pieces];
    private int _index = -1;

    public string Name => name;

    public void Do(CadDocument doc)
    {
        _index = doc.Entities.IndexOf(original);
        if (_index < 0) return;
        doc.Entities.RemoveAt(_index);
        doc.Entities.InsertRange(_index, _pieces);
    }

    public void Undo(CadDocument doc)
    {
        if (_index < 0) return;
        doc.Entities.RemoveRange(_index, _pieces.Count);
        doc.Entities.Insert(_index, original);
    }
}

/// <summary>
/// 表示の順番: 図形を最前面 (並びの最後) / 最背面 (並びの最初) へ。画像は最前面にすると線・文字より上、最背面にすると一番下になる。
/// </summary>
public sealed class DrawOrderCommand(IEnumerable<Entity> entities, bool toFront) : IUndoableCommand
{
    private readonly List<Entity> _targets = [.. entities];
    private List<Entity> _oldOrder = [];
    private readonly Dictionary<ImageEntity, bool> _oldFront = [];

    public string Name => toFront ? "最前面へ" : "最背面へ";

    public void Do(CadDocument doc)
    {
        _oldOrder = [.. doc.Entities];
        var set = _targets.ToHashSet();
        var moved = doc.Entities.Where(set.Contains).ToList(); // 選んだ図形どうしの順番は保つ
        var rest = doc.Entities.Where(e => !set.Contains(e)).ToList();
        foreach (var img in moved.OfType<ImageEntity>())
        {
            _oldFront[img] = img.InFront;
            img.InFront = toFront;
        }
        doc.Entities.Clear();
        doc.Entities.AddRange(toFront ? rest.Concat(moved) : moved.Concat(rest));
        doc.NotifyChanged();
    }

    public void Undo(CadDocument doc)
    {
        foreach (var (img, front) in _oldFront) img.InFront = front;
        doc.Entities.Clear();
        doc.Entities.AddRange(_oldOrder);
        doc.NotifyChanged();
    }
}

/// <summary>測量座標の向き (座標設定で図面を回さずに持つ角度) を変える。</summary>
public sealed class SetSurveyFrameCommand(double rotation, Vec2 pivot) : IUndoableCommand
{
    private double _oldRotation;
    private Vec2 _oldPivot;
    public string Name => "測量座標の向き";

    public void Do(CadDocument doc)
    {
        (_oldRotation, _oldPivot) = (doc.SurveyRotation, doc.SurveyPivot);
        doc.SetSurveyFrame(rotation, pivot);
    }

    public void Undo(CadDocument doc) => doc.SetSurveyFrame(_oldRotation, _oldPivot);
}

/// <summary>図形の並び順をまとめて変える (座標の並べ替えなど)。図形そのものは変えない。</summary>
public sealed class ReorderEntitiesCommand(IEnumerable<Entity> newOrder, string name) : IUndoableCommand
{
    private readonly List<Entity> _new = [.. newOrder];
    private List<Entity> _old = [];

    public string Name => name;

    public void Do(CadDocument doc)
    {
        _old = [.. doc.Entities];
        doc.Entities.Clear();
        doc.Entities.AddRange(_new);
        doc.NotifyChanged();
    }

    public void Undo(CadDocument doc)
    {
        doc.Entities.Clear();
        doc.Entities.AddRange(_old);
        doc.NotifyChanged();
    }
}

/// <summary>
/// シートの縦の縮尺を変える (null なら横と同じ)。寸法の縦の倍率もシートに合わせ直す。
/// 横の縮尺を変えたあとに今の縦の縮尺で実行すると、寸法の倍率だけを合わせ直せる。
/// </summary>
public sealed class SetVerticalScaleCommand(double? verticalScaleDenominator) : IUndoableCommand
{
    private double? _old;
    private readonly Dictionary<DimensionEntity, double> _oldFactors = [];

    public string Name => verticalScaleDenominator is double v ? $"縦の縮尺 1:{v:0.##}" : "縦の縮尺 (横と同じ)";

    public void Do(CadDocument doc)
    {
        _old = doc.VerticalScaleDenominator;
        _oldFactors.Clear();
        doc.VerticalScaleDenominator = verticalScaleDenominator;
        foreach (var d in doc.Entities.OfType<DimensionEntity>())
        {
            _oldFactors[d] = d.VerticalFactor;
            d.VerticalFactor = doc.VerticalFactor;
        }
        doc.NotifyChanged();
    }

    public void Undo(CadDocument doc)
    {
        doc.VerticalScaleDenominator = _old;
        foreach (var (d, f) in _oldFactors) d.VerticalFactor = f;
        doc.NotifyChanged();
    }
}
