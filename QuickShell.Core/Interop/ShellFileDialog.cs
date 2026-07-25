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

    /// <summary>
    /// Displays a folder picker and obtains the selected folder path.
    /// </summary>
    /// <param name="owner">The handle of the parent window.</param>
    /// <param name="initialDirectory">The directory initially displayed by the picker, when valid.</param>
    /// <returns>The selected folder path, or <c>null</c> if the picker is canceled.</returns>
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
    /// <summary>
    /// Displays a file selection dialog and obtains the selected file path.
    /// </summary>
    /// <param name="owner">The handle of the dialog's owner window.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">The file type filters to display.</param>
    /// <param name="defaultExt">The default file extension.</param>
    /// <param name="initialDirectory">The directory initially displayed by the dialog.</param>
    /// <returns>The selected file path, or <c>null</c> if the dialog is canceled.</returns>
    public static string? PickOpenFile(nint owner, string title, (string Name, string Spec)[] filters, string? defaultExt, string? initialDirectory)
    {
        var dialog = (IFileOpenDialog)CreateInstance(CLSID_FileOpenDialog, IID_IFileOpenDialog);
        FilterNativeMemory? filterMemory = null;
        try
        {
            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            dialog.SetTitle(title);
            filterMemory = ApplyFilters(dialog, filters, defaultExt);
            ApplyInitialDirectory(dialog, initialDirectory);
            return ShowAndGetPath(dialog, owner);
        }
        finally
        {
            filterMemory?.Dispose();
            Release(dialog);
        }
    }

    /// <summary>
    /// Opens a save-file dialog with overwrite confirmation and optional file name, extension, filter, and initial directory settings.
    /// </summary>
    /// <param name="defaultFileName">The suggested file name.</param>
    /// <returns>The selected file's filesystem path, or <c>null</c> if the dialog is canceled.</returns>
    public static string? PickSaveFile(nint owner, string title, (string Name, string Spec)[] filters, string? defaultExt, string? defaultFileName, string? initialDirectory)
    {
        var dialog = (IFileSaveDialog)CreateInstance(CLSID_FileSaveDialog, IID_IFileSaveDialog);
        FilterNativeMemory? filterMemory = null;
        try
        {
            dialog.SetOptions(FOS_FORCEFILESYSTEM | FOS_OVERWRITEPROMPT | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            dialog.SetTitle(title);
            filterMemory = ApplyFilters(dialog, filters, defaultExt);
            if (!string.IsNullOrEmpty(defaultFileName))
            {
                dialog.SetFileName(defaultFileName);
            }

            ApplyInitialDirectory(dialog, initialDirectory);
            return ShowAndGetPath(dialog, owner);
        }
        finally
        {
            filterMemory?.Dispose();
            Release(dialog);
        }
    }

    /// <summary>
    /// Displays the file dialog and retrieves the selected item's filesystem path.
    /// </summary>
    /// <param name="dialog">The file dialog to display.</param>
    /// <param name="owner">The handle of the dialog's owner window.</param>
    /// <returns>The selected filesystem path, or <c>null</c> if the dialog is canceled or no path is available.</returns>
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

    /// <summary>
    /// Applies an existing directory as the dialog's initial folder.
    /// </summary>
    /// <param name="dialog">The file dialog to configure.</param>
    /// <param name="initialDirectory">The directory to use initially, or <see langword="null"/> to use the shell's default folder.</param>
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

    /// <summary>
    /// Builds COMDLG_FILTERSPEC native memory for <see cref="IFileDialog.SetFileTypes"/>.
    /// The returned disposable must stay alive until after <see cref="IModalWindow.Show"/>;
    /// the dialog may keep pointers into this buffer for the lifetime of the modal call.
    /// <summary>
    /// Applies file type filters and an optional default extension to a file dialog.
    /// </summary>
    /// <param name="filters">The display names and wildcard specifications for the file types.</param>
    /// <param name="defaultExt">The default file name extension.</param>
    /// <returns>Native memory that owns the applied filter specifications, or <c>null</c> when no filters are provided.</returns>
    private static FilterNativeMemory? ApplyFilters(IFileDialog dialog, (string Name, string Spec)[] filters, string? defaultExt)
    {
        if (filters.Length == 0)
        {
            if (!string.IsNullOrEmpty(defaultExt))
            {
                dialog.SetDefaultExtension(defaultExt);
            }

            return null;
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
            if (!string.IsNullOrEmpty(defaultExt))
            {
                dialog.SetDefaultExtension(defaultExt);
            }

            // Ownership transfers to the caller until Show completes.
            var memory = new FilterNativeMemory(block, strings);
            block = 0;
            strings = null;
            return memory;
        }
        finally
        {
            if (strings is not null)
            {
                foreach (nint s in strings)
                {
                    Marshal.FreeCoTaskMem(s);
                }
            }

            if (block != 0)
            {
                Marshal.FreeCoTaskMem(block);
            }
        }
    }

    private sealed class FilterNativeMemory(nint block, List<nint> strings) : IDisposable
    {
        private nint _block = block;
        private List<nint>? _strings = strings;

        /// <summary>
        /// Releases the native memory allocated for the filter specifications.
        /// </summary>
        public void Dispose()
        {
            var ownedStrings = Interlocked.Exchange(ref _strings, null);
            if (ownedStrings is not null)
            {
                foreach (nint s in ownedStrings)
                {
                    Marshal.FreeCoTaskMem(s);
                }
            }

            var ownedBlock = Interlocked.Exchange(ref _block, 0);
            if (ownedBlock != 0)
            {
                Marshal.FreeCoTaskMem(ownedBlock);
            }
        }
    }

    /// <summary>
    /// Creates and wraps an instance of the specified COM class and interface.
    /// </summary>
    /// <param name="clsid">The class identifier of the COM class to create.</param>
    /// <param name="iid">The interface identifier to expose.</param>
    /// <returns>The wrapped COM object.</returns>
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

    /// <summary>
    /// Creates a COM object for the specified class and interface.
    /// </summary>
    /// <param name="rclsid">The class identifier of the COM object to create.</param>
    /// <param name="pUnkOuter">The controlling unknown for aggregation, or zero.</param>
    /// <param name="dwClsContext">The execution context in which the COM object runs.</param>
    /// <param name="riid">The interface identifier to retrieve.</param>
    /// <param name="ppv">Receives a pointer to the requested interface.</param>
    /// <returns>An HRESULT indicating whether the object was created successfully.</returns>
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    /// <summary>
    /// Creates a Shell item from a filesystem path.
    /// </summary>
    /// <param name="pszPath">The filesystem path used to create the Shell item.</param>
    /// <param name="pbc">The optional bind context, or zero.</param>
    /// <param name="riid">The interface identifier requested for the Shell item.</param>
    /// <param name="ppv">Receives a pointer to the requested interface.</param>
    /// <returns>An HRESULT indicating whether the Shell item was created.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);

    // IModalWindow — base of IFileDialog. Only Show is called.
    [GeneratedComInterface]
    [Guid("b4db1657-70d7-485e-8e3e-6fcb5a5c1802")]
    internal partial interface IModalWindow
    {
        /// <summary>
        /// Displays the modal window.
        /// </summary>
        /// <param name="parent">The handle of the window that owns the modal window.</param>
        /// <returns>An HRESULT indicating whether the window was displayed successfully.</returns>
        [PreserveSig]
        int Show(nint parent);
    }

    // IFileDialog : IModalWindow. Declared in full vtable order; unused slots use blittable
    // [PreserveSig] signatures so we never generate marshalling for methods we don't call.
    [GeneratedComInterface]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    internal partial interface IFileDialog : IModalWindow
    {
        /// <summary>
/// Configures the file types displayed by the dialog.
/// </summary>
/// <param name="cFileTypes">The number of file type specifications.</param>
/// <param name="rgFilterSpec">A pointer to the array of file type specifications.</param>
void SetFileTypes(uint cFileTypes, nint rgFilterSpec);

        /// <summary>
/// Selects the default file type in the dialog's file type list.
/// </summary>
/// <param name="iFileType">The one-based index of the file type to select.</param>
void SetFileTypeIndex(uint iFileType);

        /// <summary>
        /// Retrieves the index of the currently selected file type.
        /// </summary>
        /// <param name="piFileType">Receives the one-based index of the selected file type.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetFileTypeIndex(out uint piFileType);

        [PreserveSig]
        int Advise(nint pfde, out uint pdwCookie);

        /// <summary>
        /// Removes the registered file dialog event handler.
        /// </summary>
        /// <param name="dwCookie">The connection cookie returned when the event handler was registered.</param>
        /// <returns>An HRESULT indicating whether the event handler was removed successfully.</returns>
        [PreserveSig]
        int Unadvise(uint dwCookie);

        /// <summary>
/// Configures the file dialog options.
/// </summary>
/// <param name="fos">The bitwise combination of file dialog option flags.</param>
void SetOptions(uint fos);

        /// <summary>
        /// Retrieves the current options configured for the file dialog.
        /// </summary>
        /// <param name="pfos">Receives the configured file dialog option flags.</param>
        /// <returns>An HRESULT indicating whether the options were retrieved successfully.</returns>
        [PreserveSig]
        int GetOptions(out uint pfos);

        /// <summary>
        /// Sets the dialog's default folder.
        /// </summary>
        /// <param name="psi">A pointer to the shell item representing the default folder.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int SetDefaultFolder(nint psi);

        /// <summary>
/// Sets the folder displayed by the file dialog.
/// </summary>
/// <param name="psi">The shell item representing the folder to display.</param>
void SetFolder(IShellItem psi);

        /// <summary>
        /// Retrieves the dialog's current folder.
        /// </summary>
        /// <param name="ppsi">Receives a pointer to the folder's shell item.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetFolder(out nint ppsi);

        /// <summary>
        /// Retrieves the shell item currently selected in the dialog.
        /// </summary>
        /// <param name="ppsi">Receives a pointer to the selected shell item.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetCurrentSelection(out nint ppsi);

        /// <summary>
/// Sets the file name displayed in the dialog.
/// </summary>
/// <param name="pszName">The file name to display.</param>
void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        /// <summary>
        /// Retrieves the current file name displayed by the dialog.
        /// </summary>
        /// <param name="pszName">Receives a pointer to the file name.</param>
        /// <returns>An HRESULT indicating whether the file name was retrieved successfully.</returns>
        [PreserveSig]
        int GetFileName(out nint pszName);

        /// <summary>
/// Sets the dialog title.
/// </summary>
void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        /// <summary>
        /// Sets the label of the dialog's OK button.
        /// </summary>
        /// <param name="pszText">A pointer to a null-terminated string containing the button label.</param>
        /// <returns>An HRESULT indicating whether the label was set successfully.</returns>
        [PreserveSig]
        int SetOkButtonLabel(nint pszText);

        /// <summary>
        /// Sets the label for the file name input control.
        /// </summary>
        /// <param name="pszLabel">A pointer to the label string.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int SetFileNameLabel(nint pszLabel);

        /// <summary>
/// Retrieves the selected shell item.
/// </summary>
/// <param name="ppsi">Receives a pointer to the selected shell item.</param>
///
void GetResult(out nint ppsi);

        /// <summary>
        /// Adds a location to the dialog's navigation pane.
        /// </summary>
        /// <param name="psi">A pointer to the shell item representing the location.</param>
        /// <param name="fdap">The placement of the location in the navigation pane.</param>
        /// <returns>An HRESULT indicating whether the location was added successfully.</returns>
        [PreserveSig]
        int AddPlace(nint psi, int fdap);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        /// <summary>
        /// Closes the dialog with the specified result code.
        /// </summary>
        /// <param name="hr">The result code to associate with the dialog closure.</param>
        /// <returns>The HRESULT returned by the operation.</returns>
        [PreserveSig]
        int Close(int hr);

        [PreserveSig]
        int SetClientGuid(in Guid guid);

        /// <summary>
        /// Clears the dialog's client data.
        /// </summary>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int ClearClientData();

        /// <summary>
        /// Sets the dialog's filter configuration.
        /// </summary>
        /// <param name="pFilter">A pointer to the filter configuration.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int SetFilter(nint pFilter);
    }

    // IFileOpenDialog : IFileDialog. Adds GetResults / GetSelectedItems (unused; single-select
    // uses IFileDialog.GetResult). Slots declared to keep the type usable if ever needed.
    [GeneratedComInterface]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    internal partial interface IFileOpenDialog : IFileDialog
    {
        /// <summary>
        /// Retrieves an enumerator for the selected items.
        /// </summary>
        /// <param name="ppenum">Receives a pointer to the selected-items enumerator.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetResults(out nint ppenum);

        /// <summary>
        /// Retrieves the items selected in the dialog.
        /// </summary>
        /// <param name="ppsai">Receives a pointer to the selected items collection.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetSelectedItems(out nint ppsai);
    }

    // IFileSaveDialog : IFileDialog. Extra slots unused (SetSaveAsItem/SetProperties/...).
    [GeneratedComInterface]
    [Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab")]
    internal partial interface IFileSaveDialog : IFileDialog
    {
        /// <summary>
        /// Sets the item to use as the save destination.
        /// </summary>
        /// <param name="psi">A pointer to the shell item.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int SetSaveAsItem(nint psi);

        [PreserveSig]
        int SetProperties(nint pStore);

        /// <summary>
        /// Configures the properties collected by the dialog.
        /// </summary>
        /// <param name="pList">A pointer to the property description list.</param>
        /// <param name="fAppendDefault">A value indicating whether to append the default properties.</param>
        /// <returns>An HRESULT indicating the operation result.</returns>
        [PreserveSig]
        int SetCollectedProperties(nint pList, int fAppendDefault);

        /// <summary>
        /// Retrieves the property store for the shell item.
        /// </summary>
        /// <param name="ppStore">Receives a pointer to the property store.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetProperties(out nint ppStore);

        /// <summary>
        /// Applies the specified property store to a shell item.
        /// </summary>
        /// <param name="psi">A pointer to the shell item.</param>
        /// <param name="pStore">A pointer to the property store containing the properties to apply.</param>
        /// <param name="hwnd">The handle of the parent window.</param>
        /// <param name="pSink">A pointer to the progress notification sink.</param>
        /// <returns>An HRESULT indicating whether the properties were applied successfully.</returns>
        [PreserveSig]
        int ApplyProperties(nint psi, nint pStore, nint hwnd, nint pSink);
    }

    [GeneratedComInterface]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    internal partial interface IShellItem
    {
        /// <summary>
        /// Binds the shell item to the specified handler and interface.
        /// </summary>
        /// <param name="pbc">The optional bind context.</param>
        /// <param name="bhid">The identifier of the handler to use.</param>
        /// <param name="riid">The identifier of the requested interface.</param>
        /// <param name="ppv">Receives a pointer to the requested interface.</param>
        /// <returns>An HRESULT indicating whether the binding succeeded.</returns>
        [PreserveSig]
        int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

        /// <summary>
        /// Retrieves the parent shell item.
        /// </summary>
        /// <param name="ppsi">Receives a pointer to the parent shell item.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetParent(out nint ppsi);

        /// <summary>
/// Retrieves the item's display name in the specified format.
/// </summary>
/// <param name="sigdnName">The display name format to retrieve.</param>
/// <param name="ppszName">Receives a pointer to the allocated display-name string.</param>
void GetDisplayName(uint sigdnName, out nint ppszName);

        /// <summary>
        /// Retrieves the specified attributes of the shell item.
        /// </summary>
        /// <param name="sfgaoMask">The attributes to retrieve.</param>
        /// <param name="psfgaoAttribs">Receives the retrieved attributes.</param>
        /// <returns>An HRESULT indicating whether the operation succeeded.</returns>
        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        /// <summary>
        /// Compares this shell item with another shell item.
        /// </summary>
        /// <param name="psi">The shell item to compare with this item.</param>
        /// <param name="hint">The comparison criteria.</param>
        /// <param name="piOrder">Receives a value indicating the relative ordering of the shell items.</param>
        /// <returns>An HRESULT indicating whether the comparison succeeded.</returns>
        [PreserveSig]
        int Compare(nint psi, uint hint, out int piOrder);
    }
}
