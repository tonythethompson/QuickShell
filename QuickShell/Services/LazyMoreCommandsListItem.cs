using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace QuickShell.Services;

/// <summary>
/// Home-list row whose context menu is built on first <see cref="MoreCommands"/> read
/// (CmdPal selection-time SlowInitialize), not during list construction.
/// </summary>
internal sealed partial class LazyMoreCommandsListItem : ListItem
{
    private readonly Func<IContextItem[]> _factory;
    private readonly object _moreCommandsGate = new();
    private IContextItem[]? _built;

    public LazyMoreCommandsListItem(ICommand command, Func<IContextItem[]> factory)
        : base(command)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// True after the factory has run or an explicit value was assigned.
    /// </summary>
    internal bool HasBuiltMoreCommands => _built is not null;

    public override IContextItem[] MoreCommands
    {
        get
        {
            lock (_moreCommandsGate)
            {
                return _built ??= _factory();
            }
        }

        set
        {
            var next = value ?? [];
            if (ReferenceEquals(_built, next))
            {
                return;
            }

            lock (_moreCommandsGate)
            {
                if (ReferenceEquals(_built, next))
                {
                    return;
                }

                _built = next;
            }

            OnPropertyChanged();
        }
    }
}
