namespace QuickShell.Services;

internal sealed class FormEditHistory<T>
    where T : class
{
    public const int MaxDepth = 25;

    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];
    private readonly Func<T, T> _clone;

    public FormEditHistory(Func<T, T> clone) => _clone = clone;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void PushBeforeChange(T snapshot)
    {
        _undo.Add(_clone(snapshot));
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public bool TryUndo(T current, out T restored)
    {
        restored = current;
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(_clone(current));
        restored = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool TryRedo(T current, out T restored)
    {
        restored = current;
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add(_clone(current));
        restored = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
