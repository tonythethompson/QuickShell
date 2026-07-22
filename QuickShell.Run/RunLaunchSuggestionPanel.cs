using QuickShell.Abstractions;
using QuickShell.Abstractions.Classification;
using QuickShell.Classification;
using QuickShell.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace QuickShell.Run;

internal sealed record RunLaunchRowSnapshot(
    string Id,
    LaunchRowKind Kind,
    string Label,
    string Command,
    string TaskType,
    string LaunchTarget,
    bool RunAsAdmin,
    bool IsEnabled,
    bool IsEditorPlaceholder = false);

internal sealed class RunLaunchSuggestionPanel
{
    private readonly StackPanel _root;
    private readonly TextBlock _loadingText;
    private readonly WrapPanel _chips;
    private readonly Button _toggleButton;
    private readonly ScrollViewer _scrollViewer;

    private IReadOnlyList<CommandSuggestionPill> _pills = [];
    private bool _expanded;
    private bool _isLoading;

    public RunLaunchSuggestionPanel()
    {
        _loadingText = new TextBlock
        {
            Text = "Scanning folder…",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed,
        };

        _chips = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 4),
        };

        _scrollViewer = new ScrollViewer
        {
            MaxHeight = 72,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Content = _chips,
            Visibility = Visibility.Collapsed,
        };

        _toggleButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        _toggleButton.Click += (_, _) =>
        {
            _expanded = !_expanded;
            RenderChips();
        };

        _root = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        _root.Children.Add(RunWpfUiHelpers.FieldLabel(
            "Suggested commands",
            WorkspaceFormTooltips.SuggestedCommands));
        _root.Children.Add(_loadingText);
        _root.Children.Add(_scrollViewer);
        _root.Children.Add(_toggleButton);
    }

    public UIElement Root => _root;

    public event Action<CommandSuggestionPill>? PillClicked;

    public void SetLoading(bool isLoading)
    {
        _isLoading = isLoading;
        _loadingText.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (isLoading)
        {
            _scrollViewer.Visibility = Visibility.Collapsed;
            _toggleButton.Visibility = Visibility.Collapsed;
            _root.Visibility = Visibility.Visible;
        }
    }

    public void SetPills(IReadOnlyList<CommandSuggestionPill> pills)
    {
        _isLoading = false;
        _loadingText.Visibility = Visibility.Collapsed;
        _pills = pills;
        _expanded = false;
        _root.Visibility = pills.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderChips();
    }

    public void Hide()
    {
        _isLoading = false;
        _pills = [];
        _root.Visibility = Visibility.Collapsed;
        _loadingText.Visibility = Visibility.Collapsed;
        _scrollViewer.Visibility = Visibility.Collapsed;
        _toggleButton.Visibility = Visibility.Collapsed;
        _chips.Children.Clear();
    }

    private void RenderChips()
    {
        _chips.Children.Clear();
        if (_pills.Count == 0)
        {
            _scrollViewer.Visibility = Visibility.Collapsed;
            _toggleButton.Visibility = Visibility.Collapsed;
            return;
        }

        var visibleCount = _expanded
            ? _pills.Count
            : Math.Min(SuggestionPillPresentation.DefaultVisibleSlots, _pills.Count);

        for (var i = 0; i < visibleCount; i++)
        {
            var pill = _pills[i];
            var button = new Button
            {
                Content = pill.DisplayTitle,
                ToolTip = pill.Tooltip,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 4, 8, 4),
                IsEnabled = !_isLoading,
            };
            button.Click += (_, _) => PillClicked?.Invoke(pill);
            _chips.Children.Add(button);
        }

        _scrollViewer.Visibility = Visibility.Visible;
        if (_pills.Count > SuggestionPillPresentation.DefaultVisibleSlots)
        {
            _toggleButton.Content = _expanded ? "Show fewer suggestions" : "Show more suggestions";
            _toggleButton.Visibility = Visibility.Visible;
        }
        else
        {
            _toggleButton.Visibility = Visibility.Collapsed;
        }
    }
}

internal sealed class RunDirectorySuggestionLoader : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private CancellationTokenSource? _debounceCts;
    private int _generation;
    private bool _disposed;

    public RunDirectorySuggestionLoader(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Schedule(
        IProjectAnalysisService projectAnalysis,
        ICommandSuggestionService commandSuggestions,
        string? directory,
        IEnumerable<string?> usedCommands,
        Action<int> onGenerationStarted,
        Func<IReadOnlyList<CommandSuggestionPill>, int, Task> onCompleted)
    {
        ArgumentNullException.ThrowIfNull(commandSuggestions);
        CancellationTokenSource cancellation;
        CancellationTokenSource? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            previous = _debounceCts;
            _debounceCts = cancellation;
        }

        previous?.Cancel();
        previous?.Dispose();
        var token = cancellation.Token;
        var generation = Interlocked.Increment(ref _generation);
        onGenerationStarted(generation);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    await _dispatcher.InvokeAsync(() => onCompleted([], generation));
                    return;
                }

                var pills = commandSuggestions.GetPills(
                    directory,
                    usedCommands,
                    projectAnalysis);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await _dispatcher.InvokeAsync(() => onCompleted(pills, generation));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await _dispatcher.InvokeAsync(() => onCompleted([], generation));
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_debounceCts, cancellation))
                    {
                        _debounceCts = null;
                    }
                }

                cancellation.Dispose();
            }
        }, token);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _debounceCts;
            _debounceCts = null;
        }

        cancellation?.Cancel();
    }
}
