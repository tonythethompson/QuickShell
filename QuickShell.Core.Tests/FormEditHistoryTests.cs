using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class FormEditHistoryTests
{
    [Fact]
    public void TryUndo_RestoresPriorSnapshot()
    {
        var history = new FormEditHistory<List<string>>(rows => [.. rows]);
        var current = new List<string> { "a" };
        history.PushBeforeChange(current);
        current.Add("b");

        Assert.True(history.TryUndo(current, out var restored));
        Assert.Equal(["a"], restored);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void TryRedo_ReappliesUndoneSnapshot()
    {
        var history = new FormEditHistory<List<string>>(rows => [.. rows]);
        var current = new List<string> { "a" };
        history.PushBeforeChange(current);
        current.Add("b");
        history.TryUndo(current, out current);

        Assert.True(history.TryRedo(current, out var restored));
        Assert.Equal(["a", "b"], restored);
    }

    [Fact]
    public void PushBeforeChange_ClearsRedoStack()
    {
        var history = new FormEditHistory<List<string>>(rows => [.. rows]);
        var current = new List<string> { "a" };
        history.PushBeforeChange(current);
        current.Add("b");
        history.TryUndo(current, out current);
        history.PushBeforeChange(current);
        current.Add("c");

        Assert.False(history.TryRedo(current, out _));
    }
}
