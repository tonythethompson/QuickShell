namespace QuickShell.Services;

/// <summary>
/// Writes files via temp + replace/move so crashes cannot leave a half-written destination.
/// </summary>
internal interface IAtomicFileWriter
{
    void WriteAllBytesAtomic(string path, byte[] contents);

    void WriteAllTextAtomic(string path, string contents);
}
