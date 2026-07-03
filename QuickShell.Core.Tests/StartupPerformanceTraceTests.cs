using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class StartupPerformanceTraceTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEnabledValue_RequiresOptIn(string? value, bool expected)
    {
        Assert.Equal(expected, StartupPerformanceTrace.IsEnabledValue(value));
    }
}
