using System.Text.Json;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class ShortcutLayoutJson
{
    public static bool TryParse(Stream stream, out List<ShortcutLayoutEntry> layout)
    {
        layout = [];

        try
        {
            using var document = JsonDocument.Parse(stream);
            return TryParseRoot(document.RootElement, out layout);
        }
        catch
        {
            layout = [];
            return false;
        }
    }

    public static bool TryParseRoot(JsonElement root, out List<ShortcutLayoutEntry> layout)
    {
        layout = [];

        if (root.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryReadSeparator(element, out var separator))
            {
                layout.Add(separator);
                continue;
            }

            var shortcut = element.Deserialize(QuickShellJsonContext.Default.TerminalShortcut);
            if (shortcut is null || string.IsNullOrWhiteSpace(shortcut.Name) || string.IsNullOrWhiteSpace(shortcut.Directory))
            {
                continue;
            }

            layout.Add(ShortcutLayoutEntry.FromShortcut(shortcut));
        }

        return true;
    }

    public static byte[] Serialize(IReadOnlyList<ShortcutLayoutEntry> layout)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            foreach (var entry in layout)
            {
                if (entry.Kind == ShortcutLayoutEntryKind.Separator)
                {
                    writer.WriteStartObject();
                    writer.WriteString("Type", "separator");
                    if (!string.IsNullOrWhiteSpace(entry.SeparatorTitle))
                    {
                        writer.WriteString("Title", entry.SeparatorTitle);
                    }

                    writer.WriteEndObject();
                    continue;
                }

                if (entry.Shortcut is null)
                {
                    continue;
                }

                JsonSerializer.Serialize(writer, entry.Shortcut, QuickShellJsonContext.Default.TerminalShortcut);
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    public static TerminalShortcut[] ExtractShortcuts(IReadOnlyList<ShortcutLayoutEntry> layout) =>
        layout
            .Where(entry => entry.Kind == ShortcutLayoutEntryKind.Shortcut && entry.Shortcut is not null)
            .Select(entry => entry.Shortcut!)
            .ToArray();

    private static bool TryReadSeparator(JsonElement element, out ShortcutLayoutEntry separator)
    {
        separator = ShortcutLayoutEntry.FromSeparator(null);

        if (!element.TryGetProperty("Type", out var typeProperty))
        {
            return false;
        }

        if (!string.Equals(typeProperty.GetString(), "separator", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? title = null;
        if (element.TryGetProperty("Title", out var titleProperty) &&
            titleProperty.ValueKind == JsonValueKind.String)
        {
            title = titleProperty.GetString();
        }

        separator = ShortcutLayoutEntry.FromSeparator(title);
        return true;
    }
}
