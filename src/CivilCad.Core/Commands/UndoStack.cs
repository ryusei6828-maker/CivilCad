using CivilCad.Core.Model;

namespace CivilCad.Core.Commands;

public interface IUndoableCommand
{
    string Name { get; }
    void Do(CadDocument doc);
    void Undo(CadDocument doc);
}

public sealed class UndoStack(CadDocument doc)
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>変更のたびに増える番号 (保存時の値と比べて「未保存の変更があるか」を判定する)。</summary>
    public long Version { get; private set; }

    public void Execute(IUndoableCommand command)
    {
        command.Do(doc);
        _undo.Push(command);
        _redo.Clear();
        Version++;
        doc.NotifyChanged();
    }

    public string? Undo()
    {
        if (!CanUndo) return null;
        var c = _undo.Pop();
        c.Undo(doc);
        _redo.Push(c);
        Version++;
        doc.NotifyChanged();
        return c.Name;
    }

    public string? Redo()
    {
        if (!CanRedo) return null;
        var c = _redo.Pop();
        c.Do(doc);
        _undo.Push(c);
        Version++;
        doc.NotifyChanged();
        return c.Name;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
