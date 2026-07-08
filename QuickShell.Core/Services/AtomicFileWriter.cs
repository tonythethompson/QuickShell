using System.Text;

namespace QuickShell.Services;

/// <summary>
/// Shared atomic writer matching <c>ShortcutRepository.WriteLayoutAtomic</c> semantics:
/// write <c>path.tmp</c>, then <see cref="File.Replace"/> (with <c>.bak</c>) or <see cref="File.Move"/>.
/// Process-wide locks (e.g. the shortcuts mutex) stay with the caller.
/// </summary>
internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void WriteAllTextAtomic(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes(contents));
    }

    public void WriteAllBytesAtomic(string path, byte[] contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        try
        {
            File.WriteAllBytes(tempPath, contents);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
