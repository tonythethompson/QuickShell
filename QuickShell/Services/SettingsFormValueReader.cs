using System.Text.Json.Nodes;

namespace QuickShell.Services;

internal static class SettingsFormValueReader
{
    internal static string? ReadString(JsonObject? values, string fieldName)
    {
        if (values is null || !values.TryGetPropertyValue(fieldName, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        var raw = node.ToString().Trim();
        if (raw.Length >= 2 && raw.StartsWith('"') && raw.EndsWith('"'))
        {
            raw = raw[1..^1];
        }

        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
