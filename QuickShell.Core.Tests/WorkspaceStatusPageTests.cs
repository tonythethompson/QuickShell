using QuickShell.Pages;
using System.Reflection;
using System.Text.Json;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceStatusPageTests
{
    [Fact]
    public void BuildTemplate_EscapesInterpolatedDiagnosticsTitles()
    {
        var template = InvokePrivateStaticString("BuildTemplate");

        using var document = JsonDocument.Parse(template);
        Assert.Equal("AdaptiveCard", document.RootElement.GetProperty("type").GetString());
        Assert.DoesNotContain("{{Strings.", template, StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_HandlesJsonControlCharacters()
    {
        var escape = typeof(WorkspaceStatusForm).GetMethod(
            "Escape",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(escape);

        var result = Assert.IsType<string>(escape.Invoke(null, ["quote\" slash\\ newline\n"]));

        Assert.Equal("quote\\u0022 slash\\\\ newline\\n", result);
    }

    private static string InvokePrivateStaticString(string methodName)
    {
        var method = typeof(WorkspaceStatusForm).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, null));
    }
}
