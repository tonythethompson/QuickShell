using System.Text;

namespace QuickShell.Services;

/// <summary>
/// Hex-UTF8 encoding shared by command ID builders and the parser.
/// </summary>
internal static class CommandIdEncoding
{
    public static string EncodeNameKey(string name) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(name)).ToLowerInvariant();

    public static string EncodeDirectoryKey(string directory)
    {
        var normalized = directory.Trim().TrimEnd('\\', '/');
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            // Keep trimmed input when full path normalization fails.
        }

        return EncodeNameKey(normalized);
    }

    public static bool TryDecodeHexUtf8(string encoded, out string value)
    {
        value = string.Empty;

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromHexString(encoded));
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
        {
            return false;
        }
    }
}
