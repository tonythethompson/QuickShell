using System.Text.Json.Serialization;

namespace QuickShell.Models;

/// <summary>
/// JSON shape for a task-type picker choice (title/value/tooltip). A named type so
/// <see cref="QuickShell.Classification.ProjectAnalysisService"/> can serialize it through the
/// source-generated <see cref="QuickShell.QuickShellJsonContext"/> instead of
/// System.Text.Json's reflection-based Serialize&lt;T&gt;, which trimming forbids.
/// <see cref="JsonPropertyNameAttribute"/> preserves the exact lowercase JSON keys the
/// previous anonymous-type serialization produced (the CmdPal host expects this casing).
/// </summary>
internal readonly record struct TaskTypeChoiceJson(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("tooltip")] string Tooltip);
