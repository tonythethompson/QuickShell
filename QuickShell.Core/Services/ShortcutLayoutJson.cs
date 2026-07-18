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

    public static async Task<(bool Success, List<ShortcutLayoutEntry> Layout)> TryParseAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return TryParseRoot(document.RootElement, out var layout)
                ? (true, layout)
                : (false, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (false, []);
        }
    }

    public static bool TryParseRoot(JsonElement root, out List<ShortcutLayoutEntry> layout)
    {
        layout = [];

        return root.ValueKind switch
        {
            JsonValueKind.Array => TryParseEntries(root, out layout),
            JsonValueKind.Object => TryParseEnvelope(root, out layout),
            _ => false,
        };
    }

    public static byte[] Serialize(
        IReadOnlyList<ShortcutLayoutEntry> layout,
        bool includeSecurity = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", PersistenceVersion.Current);
            writer.WritePropertyName("entries");
            WriteEntries(writer, layout, includeSecurity);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static TerminalShortcut[] ExtractShortcuts(IReadOnlyList<ShortcutLayoutEntry> layout) =>
        layout
            .Where(entry => entry.Kind == ShortcutLayoutEntryKind.Shortcut && entry.Shortcut is not null)
            .Select(entry => entry.Shortcut!)
            .ToArray();

    private static bool TryParseEnvelope(JsonElement root, out List<ShortcutLayoutEntry> layout)
    {
        layout = [];

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (root.TryGetProperty("version", out var versionProperty) &&
            versionProperty.ValueKind == JsonValueKind.Number &&
            versionProperty.TryGetInt32(out var version) &&
            version > PersistenceVersion.Current)
        {
            return false;
        }

        return TryParseEntries(entries, out layout);
    }

    private static bool TryParseEntries(JsonElement entries, out List<ShortcutLayoutEntry> layout)
    {
        layout = [];

        if (entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var element in entries.EnumerateArray())
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

            var security = TryReadSecurity(element);
            var workspaceElement = element;
            if (element.TryGetProperty("Workspace", out var wrappedWorkspace) &&
                wrappedWorkspace.ValueKind == JsonValueKind.Object)
            {
                workspaceElement = wrappedWorkspace;
            }

            var shortcut = workspaceElement.Deserialize(QuickShellJsonContext.Default.TerminalShortcut);
            if (shortcut is null || string.IsNullOrWhiteSpace(shortcut.Name) || string.IsNullOrWhiteSpace(shortcut.Directory))
            {
                continue;
            }

            layout.Add(ShortcutLayoutEntry.FromShortcut(shortcut, security));
        }

        return true;
    }

    private static void WriteEntries(
        Utf8JsonWriter writer,
        IReadOnlyList<ShortcutLayoutEntry> layout,
        bool includeSecurity)
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

            if (!includeSecurity)
            {
                JsonSerializer.Serialize(writer, entry.Shortcut, QuickShellJsonContext.Default.TerminalShortcut);
                continue;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("Workspace");
            JsonSerializer.Serialize(writer, entry.Shortcut, QuickShellJsonContext.Default.TerminalShortcut);
            writer.WritePropertyName("Security");
            JsonSerializer.Serialize(
                writer,
                entry.Security ?? new WorkspaceSecurityMetadata(),
                QuickShellJsonContext.Default.WorkspaceSecurityMetadata);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static WorkspaceSecurityMetadata? TryReadSecurity(JsonElement element)
    {
        if (!element.TryGetProperty("Security", out var securityElement) ||
            securityElement.ValueKind != JsonValueKind.Object)
        {
            return new WorkspaceSecurityMetadata();
        }

        try
        {
            var security = securityElement.Deserialize(QuickShellJsonContext.Default.WorkspaceSecurityMetadata);
            return security is null
                ? new WorkspaceSecurityMetadata()
                : new WorkspaceSecurityMetadata
                {
                    IsTrusted = security.IsTrusted,
                    Revision = security.Revision <= 0 ? 1 : security.Revision,
                };
        }
        catch
        {
            return new WorkspaceSecurityMetadata();
        }
    }

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
