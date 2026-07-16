using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class FavoriteMoveTests : IDisposable
{
    private readonly string _configDirectory;
    private readonly ShortcutRepository _repository;

    public FavoriteMoveTests()
    {
        _configDirectory = Path.Combine(Path.GetTempPath(), "qs-fav-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDirectory);
        _repository = new ShortcutRepository(_configDirectory);

        SeedFavorite("Alpha", pinOrder: 1);
        SeedFavorite("Beta", pinOrder: 2);
        SeedFavorite("Gamma", pinOrder: 3);
    }

    public void Dispose()
    {
        _repository.Dispose();
        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void MovePinned_Down_ReordersFavoritesByPinOrder()
    {
        Assert.True(_repository.MovePinned("Alpha", +1));

        var pinned = GetPinnedNames();
        Assert.Equal(["Beta", "Alpha", "Gamma"], pinned);
    }

    [Fact]
    public void MovePinned_Up_ReordersFavoritesByPinOrder()
    {
        Assert.True(_repository.MovePinned("Gamma", -1));

        var pinned = GetPinnedNames();
        Assert.Equal(["Alpha", "Gamma", "Beta"], pinned);
    }

    [Fact]
    public void MovePinnedToEdge_ToTop_And_ToBottom()
    {
        Assert.True(_repository.MovePinnedToEdge("Gamma", toTop: true));
        Assert.Equal(["Gamma", "Alpha", "Beta"], GetPinnedNames());

        Assert.True(_repository.MovePinnedToEdge("Gamma", toTop: false));
        Assert.Equal(["Alpha", "Beta", "Gamma"], GetPinnedNames());
    }

    [Fact]
    public void MovePinnedById_ToTop_ReordersAndKeepsPinned()
    {
        var gamma = _repository.GetShortcuts().Single(s => s.Name == "Gamma");
        Assert.True(_repository.MovePinnedToEdgeById(gamma.Id, toTop: true));

        var pinned = _repository.GetShortcuts()
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ToList();
        Assert.Equal(["Gamma", "Alpha", "Beta"], pinned.Select(s => s.Name).ToList());
        Assert.All(pinned, s => Assert.True(s.IsPinned));
        Assert.Equal(1, pinned[0].PinOrder);
        Assert.Equal(2, pinned[1].PinOrder);
        Assert.Equal(3, pinned[2].PinOrder);
    }

    [Fact]
    public void MovePinned_AtEdge_ReturnsFalse()
    {
        Assert.False(_repository.MovePinned("Alpha", -1));
        Assert.False(_repository.MovePinnedToEdge("Alpha", toTop: true));
        Assert.Equal(["Alpha", "Beta", "Gamma"], GetPinnedNames());
    }

    [Fact]
    public void ForShortcut_ReportsMoveVisibilityForMiddleFavorite()
    {
        var pinned = _repository.GetShortcuts()
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ToList();
        var middle = pinned.Single(s => s.Name == "Beta");

        var visibility = PinnedMoveVisibility.ForShortcut(middle, pinned);
        Assert.True(visibility.ShowUp);
        Assert.True(visibility.ShowDown);
        Assert.True(visibility.ShowToTop);
        Assert.True(visibility.ShowToBottom);
    }

    private void SeedFavorite(string name, int pinOrder)
    {
        var dir = Path.Combine(_configDirectory, name);
        Directory.CreateDirectory(dir);
        _repository.Upsert(new TerminalShortcut
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Directory = dir,
            IsPinned = true,
            PinOrder = pinOrder,
        });
    }

    private List<string> GetPinnedNames() =>
        _repository.GetShortcuts()
            .Where(s => s.IsPinned)
            .OrderBy(s => s.PinOrder ?? int.MaxValue)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Name)
            .ToList();
}
