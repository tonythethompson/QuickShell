using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class LaunchCommandSanityTests
{
    [Theory]
    [InlineData("dotnet watch run --project ${workspaceFolder}/Trackdub.sln")]
    [InlineData("dotnet watch --project tmp_serilog_probe.csproj")]
    [InlineData("npm run build -- $(workspaceFolder)")]
    [InlineData("echo %workspaceFolder%")]
    public void IsUsableSuggestion_RejectsUnexpandedOrTempCommands(string command)
    {
        Assert.False(LaunchCommandSanity.IsUsableSuggestion(command));
    }

    [Theory]
    [InlineData("dotnet watch")]
    [InlineData("dotnet watch --project Trackdub.Api.csproj")]
    [InlineData("npm run dev")]
    [InlineData("claude")]
    public void IsUsableSuggestion_AcceptsNormalCommands(string command)
    {
        Assert.True(LaunchCommandSanity.IsUsableSuggestion(command));
    }

    [Theory]
    [InlineData("tmp_serilog_probe.csproj")]
    [InlineData("temp_foo.csproj")]
    [InlineData("MyApp_probe.csproj")]
    public void IsUsableDotNetProjectFileName_RejectsTempProbes(string fileName)
    {
        Assert.False(LaunchCommandSanity.IsUsableDotNetProjectFileName(fileName));
    }

    [Theory]
    [InlineData("Trackdub.Api.csproj")]
    [InlineData("QuickShell.csproj")]
    public void IsUsableDotNetProjectFileName_AcceptsRealProjects(string fileName)
    {
        Assert.True(LaunchCommandSanity.IsUsableDotNetProjectFileName(fileName));
    }
}
