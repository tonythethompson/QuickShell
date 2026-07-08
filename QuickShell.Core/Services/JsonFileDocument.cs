using System.Text.Json;

namespace QuickShell.Services;

internal static class JsonFileDocument
{
    public static JsonDocument Parse(string path) => Parse(path, default);

    public static JsonDocument Parse(string path, JsonDocumentOptions options)
    {
        using var stream = File.OpenRead(path);
        if (IsUtf8Compatible(stream))
        {
            return JsonDocument.Parse(stream, options);
        }

        // UTF-16/UTF-32 BOM: preserve File.ReadAllText encoding detection (previous behavior).
        return JsonDocument.Parse(File.ReadAllText(path), options);
    }

    private static bool IsUtf8Compatible(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        var read = stream.Read(header);
        stream.Position = 0;

        if (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
        {
            return true;
        }

        if (read >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0xFE && header[3] == 0xFF)
        {
            return false;
        }

        if (read >= 4 && header[0] == 0xFF && header[1] == 0xFE && header[2] == 0x00 && header[3] == 0x00)
        {
            return false;
        }

        if (read >= 2 && header[0] == 0xFE && header[1] == 0xFF)
        {
            return false;
        }

        if (read >= 2 && header[0] == 0xFF && header[1] == 0xFE)
        {
            return false;
        }

        return true;
    }
}
