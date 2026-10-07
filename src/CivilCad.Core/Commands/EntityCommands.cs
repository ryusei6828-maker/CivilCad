using CivilCad.Core.Geometry;
using CivilCad.Core.Model;

namespace CivilCad.Core.Commands;

public sealed class AddEntitiesCommand(IEnumerable<Entity> entities, string name = "追加") : IUndoableCommand
{
    private readonly List<Entity> _entities = [.. entities];
    public string Name => name;
    public void Do(CadDocument doc) => doc.Entities.AddRange(_entities);

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
