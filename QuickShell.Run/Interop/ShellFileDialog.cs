using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace QuickShell.Interop;

/// <summary>
/// Minimal wrapper over the Win32 shell Common Item Dialog (IFileOpenDialog /
/// IFileSaveDialog). Replaces the WinForms FolderBrowserDialog / OpenFileDialog /
/// SaveFileDialog so the packaged app no longer forces UseWindowsForms (which blocks
/// trimming). Calls are synchronous and modal — IFileDialog.Show blocks the calling
/// thread exactly like the old ShowDialog, so the existing background-STA-thread
/// callers keep working unchanged.
///
/// Uses source-generated COM (<see cref="GeneratedComInterfaceAttribute"/>) so the
/// interop stays AOT/trim-safe (the project is IsAotCompatible=true). The full
/// IFileDialog vtable is declared in order; slots we never call are declared with
/// blittable [PreserveSig] signatures purely to preserve vtable layout.
/// </summary>
internal static partial class ShellFileDialog
{
    // A single StrategyBasedComWrappers marshals the raw IUnknown pointers returned by
    // CoCreateInstance / SHCreateItemFromParsingName into our generated interfaces.
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly Guid IID_IFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
    private static readonly Guid IID_IFileSaveDialog = new("84bccd23-5fde-4cdb-aea4-af64b83d78ab");
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    private const uint FOS_OVERWRITEPROMPT = 0x00000002;
    private const uint FOS_NOCHANGEDIR = 0x00000008;
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_FILEMUSTEXIST = 0x00001000;

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    // HRESULT_FROM_WIN32(ERROR_CANCELLED) — returned by Show when the user cancels.
    private const int ERROR_CANCELLED_HRESULT = unchecked((int)0x800704C7);

    /// <summary>Modern folder picker (IFileOpenDialog with FOS_PICKFOLDERS).</summary>
    public static string? PickFolder(nint owner, string? initialDirectory)
    {
        var dialog = (IFileOpenDialog)CreateInstance(CLSID_FileOpenDialog, IID_IFileOpenDialog);
        try
        {
            dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            ApplyInitialDirectory(dialog, initialDirectory);
            return ShowAndGetPath(dialog, owner);
        }
        finally
        {
            Release(dialog);
        }
    }

    /// <summary>Open-file picker. <paramref name="filters"/> pairs display name to a
    /// semicolon-delimited pattern (e.g. "JSON files (*.json)" -> "*.json").</summary>
    public static string? PickOpenFile(nint owner, string title, (string Name, string Spec)[] filters, string? defaultExt, string? initialDirectory)
    {
        var dialog = (IFileOpenDialog)CreateInstance(CLSID_FileOpenDialog, IID_IFileOpenDialog);
        try
        {
            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            dialog.SetTitle(title);
            ApplyFilters(dialog, filters, defaultExt);
            ApplyInitialDirectory(dialog, initialDirectory);
            return ShowAndGetPath(dialog, owner);
        }
        finally
        {
            Release(dialog);
        }
    }

    /// <summary>Save-file picker with overwrite prompt and a suggested file name.</summary>
    public static string? PickSaveFile(nint owner, string title, (string Name, string Spec)[] filters, string? defaultExt, string? defaultFileName, string? initialDirectory)
    {
        var dialog = (IFileSaveDialog)CreateInstance(CLSID_FileSaveDialog, IID_IFileSaveDialog);
        try
        {
            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_OVERWRITEPROMPT | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            dialog.SetTitle(title);
            ApplyFilters(dialog, filters, defaultExt);
            if (!string.IsNullOrEmpty(defaultFileName))
            {
                dialog.SetFileName(defaultFileName);
            }

            ApplyInitialDirectory(dialog, initialDirectory);
            return ShowAndGetPath(dialog, owner);
        }
        finally
        {
            Release(dialog);
        }
    }

    private static string? ShowAndGetPath(IFileDialog dialog, nint owner)
    {
        int hr = dialog.Show(owner);
        if (hr == ERROR_CANCELLED_HRESULT)
        {
            return null;
        }

        Marshal.ThrowExceptionForHR(hr);

        dialog.GetResult(out nint itemPtr);
        if (itemPtr == 0)
        {
            return null;
        }

        var item = (IShellItem)ComWrappers.GetOrCreateObjectForComInstance(itemPtr, CreateObjectFlags.UniqueInstance);
        Marshal.Release(itemPtr);
        try
        {
            item.GetDisplayName(SIGDN_FILESYSPATH, out nint pathPtr);
            if (pathPtr == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
        finally
        {
            Release(item);
        }
    }

    private static void ApplyInitialDirectory(IFileDialog dialog, string? initialDirectory)
    {
        if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
        {
            return;
        }

        int hr = SHCreateItemFromParsingName(initialDirectory!, 0, in IID_IShellItem, out nint itemPtr);
        if (hr < 0 || itemPtr == 0)
        {
            return; // Non-fatal: fall back to the shell's default folder.
        }

        var item = (IShellItem)ComWrappers.GetOrCreateObjectForComInstance(itemPtr, CreateObjectFlags.UniqueInstance);
        Marshal.Release(itemPtr);
        try
        {
            dialog.SetFolder(item);
        }
        finally
        {
            Release(item);
        }
    }

    private static void ApplyFilters(IFileDialog dialog, (string Name, string Spec)[] filters, string? defaultExt)
    {
        if (filters.Length == 0)
        {
            return;
        }

        // COMDLG_FILTERSPEC is { LPWSTR pszName; LPWSTR pszSpec; }. Build the native array
        // by hand so the interface method stays blittable (no struct marshalling in the vtable).
        nint block = Marshal.AllocCoTaskMem(filters.Length * nint.Size * 2);
        var strings = new List<nint>(filters.Length * 2);
        try
        {
            for (int i = 0; i < filters.Length; i++)
            {
                nint namePtr = Marshal.StringToCoTaskMemUni(filters[i].Name);
                nint specPtr = Marshal.StringToCoTaskMemUni(filters[i].Spec);
                strings.Add(namePtr);
                strings.Add(specPtr);
                Marshal.WriteIntPtr(block, i * nint.Size * 2, namePtr);
                Marshal.WriteIntPtr(block, (i * nint.Size * 2) + nint.Size, specPtr);
            }

            dialog.SetFileTypes((uint)filters.Length, block);
            dialog.SetFileTypeIndex(1);
        }
        finally
        {
            foreach (nint s in strings)
            {
                Marshal.FreeCoTaskMem(s);
            }

            Marshal.FreeCoTaskMem(block);
        }

        if (!string.IsNullOrEmpty(defaultExt))
        {
            dialog.SetDefaultExtension(defaultExt);
        }
    }

    private static object CreateInstance(Guid clsid, Guid iid)
    {
        int hr = CoCreateInstance(in clsid, 0, CLSCTX_INPROC_SERVER, in iid, out nint ptr);
        Marshal.ThrowExceptionForHR(hr);
        object instance = ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        Marshal.Release(ptr);
        return instance;
    }

    // Source-generated COM wrappers (UniqueInstance) release their underlying COM reference
    // on Dispose — Marshal.FinalReleaseComObject does not work with them (SYSLIB1099).
    private static void Release(object comObject) => (comObject as IDisposable)?.Dispose();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);

    // IModalWindow — base of IFileDialog. Only Show is called.
    [GeneratedComInterface]
    [Guid("b4db1657-70d7-485e-8e3e-6fcb5a5c1802")]
    internal partial interface IModalWindow
    {
        [PreserveSig]
        int Show(nint parent);
    }

    // IFileDialog : IModalWindow. Declared in full vtable order; unused slots use blittable
    // [PreserveSig] signatures so we never generate marshalling for methods we don't call.
    [GeneratedComInterface]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    internal partial interface IFileDialog : IModalWindow
    {
        void SetFileTypes(uint cFileTypes, nint rgFilterSpec);

        void SetFileTypeIndex(uint iFileType);

        [PreserveSig]
        int GetFileTypeIndex(out uint piFileType);

        [PreserveSig]
        int Advise(nint pfde, out uint pdwCookie);

        [PreserveSig]
        int Unadvise(uint dwCookie);

        void SetOptions(uint fos);

        [PreserveSig]
        int GetOptions(out uint pfos);

        [PreserveSig]
        int SetDefaultFolder(nint psi);

        void SetFolder(IShellItem psi);

        [PreserveSig]
        int GetFolder(out nint ppsi);

        [PreserveSig]
        int GetCurrentSelection(out nint ppsi);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetFileName(out nint pszName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        [PreserveSig]
        int SetOkButtonLabel(nint pszText);

        [PreserveSig]
        int SetFileNameLabel(nint pszLabel);

        void GetResult(out nint ppsi);

        [PreserveSig]
        int AddPlace(nint psi, int fdap);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        [PreserveSig]
        int Close(int hr);

        [PreserveSig]
        int SetClientGuid(in Guid guid);

        [PreserveSig]
        int ClearClientData();

        [PreserveSig]
        int SetFilter(nint pFilter);
    }

    // IFileOpenDialog : IFileDialog. Adds GetResults / GetSelectedItems (unused; single-select
    // uses IFileDialog.GetResult). Slots declared to keep the type usable if ever needed.
    [GeneratedComInterface]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    internal partial interface IFileOpenDialog : IFileDialog
    {
        [PreserveSig]
        int GetResults(out nint ppenum);

        [PreserveSig]
        int GetSelectedItems(out nint ppsai);
    }

    // IFileSaveDialog : IFileDialog. Extra slots unused (SetSaveAsItem/SetProperties/...).
    [GeneratedComInterface]
    [Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab")]
    internal partial interface IFileSaveDialog : IFileDialog
    {
        [PreserveSig]
        int SetSaveAsItem(nint psi);

        [PreserveSig]
        int SetProperties(nint pStore);

        [PreserveSig]
        int SetCollectedProperties(nint pList, int fAppendDefault);

        [PreserveSig]
        int GetProperties(out nint ppStore);

        [PreserveSig]
        int ApplyProperties(nint psi, nint pStore, nint hwnd, nint pSink);
    }

    [GeneratedComInterface]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    internal partial interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

        [PreserveSig]
        int GetParent(out nint ppsi);

        void GetDisplayName(uint sigdnName, out nint ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(nint psi, uint hint, out int piOrder);
    }
}
