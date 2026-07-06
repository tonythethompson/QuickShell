using QuickShell.Services;



namespace QuickShell.Core.Tests;



public sealed class RunGlobalQueryTests

{

    [Theory]

    [InlineData("quickshell")]

    [InlineData("Quick Shell")]

    [InlineData("quick-shell")]

    [InlineData("qs")]

    public void TryActivate_PluginName_EntersBrowseMode(string query)

    {

        Assert.True(RunGlobalQuery.TryActivate(query, rawQuery: null, out var remaining));

        Assert.Equal(string.Empty, remaining);

    }



    [Fact]

    public void TryActivate_PluginNameWithFilter_StripsPrefix()

    {

        Assert.True(RunGlobalQuery.TryActivate("quick shell api", rawQuery: null, out var remaining));

        Assert.Equal("api", remaining);

    }



    [Fact]

    public void TryActivate_RawQueryFallback_ActivatesWhenSearchEmpty()

    {

        Assert.True(RunGlobalQuery.TryActivate(string.Empty, "Quick Shell", out var remaining));

        Assert.Equal(string.Empty, remaining);

    }



    [Fact]

    public void TryActivate_UnrelatedQuery_DoesNotActivate()

    {

        Assert.False(RunGlobalQuery.TryActivate("my api project", rawQuery: null, out var remaining));

        Assert.Equal("my api project", remaining);

    }



    [Fact]

    public void ShouldSuppressEmptyGlobalQuery_BlocksBareGlobalSearch()

    {

        var context = new QueryActivationContext(HasActionKeyword: false, Search: string.Empty);

        Assert.True(RunGlobalQuery.ShouldSuppressEmptyGlobalQuery(context));

    }



    [Fact]

    public void ShouldSuppressEmptyGlobalQuery_AllowsActionKeywordBrowse()

    {

        var context = new QueryActivationContext(HasActionKeyword: true, Search: string.Empty);

        Assert.False(RunGlobalQuery.ShouldSuppressEmptyGlobalQuery(context));

    }

}


