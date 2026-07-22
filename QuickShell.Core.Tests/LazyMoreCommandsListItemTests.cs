using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickShell.Commands;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class LazyMoreCommandsListItemTests
{
    [Fact]
    public void MoreCommands_DoesNotInvokeFactory_UntilFirstGet()
    {
        var factoryCalls = 0;
        IContextItem[] built =
        [
            new CommandContextItem(new NoOpCommand()) { Title = "One" },
        ];

        var item = new LazyMoreCommandsListItem(
            new NoOpCommand(),
            () =>
            {
                factoryCalls++;
                return built;
            });

        Assert.False(item.HasBuiltMoreCommands);
        Assert.Equal(0, factoryCalls);

        var first = item.MoreCommands;
        Assert.Same(built, first);
        Assert.Equal(1, factoryCalls);
        Assert.True(item.HasBuiltMoreCommands);

        var second = item.MoreCommands;
        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void MoreCommands_Setter_RaisesPropChanged_AndBypassesFactory()
    {
        var factoryCalls = 0;
        var propChangedCount = 0;
        var item = new LazyMoreCommandsListItem(
            new NoOpCommand(),
            () =>
            {
                factoryCalls++;
                return [];
            });
        item.PropChanged += (_, _) => propChangedCount++;

        IContextItem[] assigned =
        [
            new CommandContextItem(new NoOpCommand()) { Title = "Assigned" },
        ];
        item.MoreCommands = assigned;

        Assert.True(item.HasBuiltMoreCommands);
        Assert.Same(assigned, item.MoreCommands);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(1, propChangedCount);

        item.MoreCommands = assigned;
        Assert.Equal(1, propChangedCount);
    }
}
