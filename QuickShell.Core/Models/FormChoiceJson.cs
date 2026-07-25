using System.Text.Json.Serialization;

namespace QuickShell.Models;

/// <summary>
/// JSON shape for a CmdPal dynamic-choice-set entry (title/value pair). A named type so
/// <see cref="QuickShell.Services.CompanionAppCatalog"/> can serialize it through the
/// source-generated <see cref="QuickShell.QuickShellJsonContext"/> instead of
/// System.Text.Json's reflection-based Serialize&lt;T&gt;, which trimming forbids.
/// <see cref="JsonPropertyNameAttribute"/> preserves the exact lowercase JSON keys the
/// previous anonymous-type serialization produced (the CmdPal host expects this casing).
/// </summary>
internal readonly record struct FormChoiceJson(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("value")] string Value);
