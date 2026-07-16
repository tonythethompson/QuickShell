using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Regression: home search must key off the last applied query, not host oldSearch.
/// Comparing only oldSearch/newSearch (or wiping via SetSearchNoUpdate) made typing a no-op.
/// </summary>
public sealed class ListSearchQueryTests
{
    [Theory]
    [InlineData("", "a")]
    [InlineData("a", "ab")]
    [InlineData("ab", "")]
    [InlineData("alpha", "Alpha")] // ordinal — case change is a real query change
    public void HasChanged_WhenIncomingDiffers_ReturnsTrue(string applied, string incoming)
    {
        Assert.True(ListSearchQuery.HasChanged(applied, incoming));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("foo", "foo")]
    public void HasChanged_WhenIncomingMatchesApplied_ReturnsFalse(string applied, string incoming)
    {
        Assert.False(ListSearchQuery.HasChanged(applied, incoming));
    }

    [Fact]
    public void HasChanged_NullIncomingMatchesEmptyApplied()
    {
        Assert.False(ListSearchQuery.HasChanged(string.Empty, null));
        Assert.True(ListSearchQuery.HasChanged("foo", null));
    }

    [Fact]
    public void HasChanged_DoesNotUseHostOldSearchSemantics()
    {
        // Host can report oldSearch == newSearch after an extension-side wipe while
        // the page still has an empty applied query and the box shows "qs".
        const string appliedQuery = "";
        const string hostOldSearch = "qs";
        const string hostNewSearch = "qs";

        // Wrong (historical) check: only old vs new → skip filter.
        var wouldSkipWithOldSearchCompare = string.Equals(
            hostOldSearch,
            hostNewSearch,
            StringComparison.Ordinal);
        Assert.True(wouldSkipWithOldSearchCompare);

        // Correct check: applied vs incoming → still filter.
        Assert.True(ListSearchQuery.HasChanged(appliedQuery, hostNewSearch));
    }

    [Fact]
    public void Normalize_MapsNullToEmpty()
    {
        Assert.Equal(string.Empty, ListSearchQuery.Normalize(null));
        Assert.Equal("x", ListSearchQuery.Normalize("x"));
    }
}
