namespace OpenCad.Core.Editing;

public interface ICadCommand
{
    string Name { get; }
    void Do();
    void Undo();
}

/// <summary>Lệnh tổng quát: hai delegate thuận/nghịch.</summary>
public sealed class DelegateCommand : ICadCommand
{
    private readonly Action _do;
    private readonly Action _undo;

    public DelegateCommand(string name, Action doAction, Action undoAction)
    {
        Name = name;
        _do = doAction;
        _undo = undoAction;
    }

    public string Name { get; }
    public void Do() => _do();
    public void Undo() => _undo();
}

/// <summary>Gộp nhiều lệnh thành một bước undo duy nhất.</summary>
public sealed class CompositeCommand : ICadCommand
{
    private readonly List<ICadCommand> _commands;

    public CompositeCommand(string name, IEnumerable<ICadCommand> commands)
    {
        Name = name;
        _commands = commands.ToList();
    }

    public string Name { get; }

    public void Do()
    {
        foreach (var c in _commands) c.Do();
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
    }
}

/// <summary>Ngăn xếp undo/redo.</summary>
public sealed class UndoStack
{
    private readonly List<ICadCommand> _undo = new();
    private readonly List<ICadCommand> _redo = new();
    private int _savePoint;

    public int Capacity { get; init; } = 200;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;
    public int Depth => _undo.Count;

    /// <summary>True khi mọi thay đổi kể từ lần lưu cuối đã được undo hết.</summary>
    public bool IsAtSavePoint => _undo.Count == _savePoint;

    /// <summary>Đánh dấu trạng thái hiện tại là "đã lưu".</summary>
    public void MarkSavePoint() => _savePoint = _undo.Count;

    public event Action? Changed;

    /// <summary>Thực thi lệnh rồi ghi vào lịch sử.</summary>
    public void Execute(ICadCommand command)
    {
        command.Do();
        Record(command);
    }

    /// <summary>Ghi một lệnh ĐÃ được thực thi (dùng cho thao tác kéo chuột trực tiếp).</summary>
    public void Record(ICadCommand command)
    {
        _undo.Add(command);
        if (_undo.Count > Capacity) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (!CanUndo) return false;

        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;

        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Do();
        _undo.Add(command);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savePoint = 0;
        Changed?.Invoke();
    }
}
