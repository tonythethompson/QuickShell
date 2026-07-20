using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using QuickShell.Abstractions;
using QuickShell.Models;
using QuickShell.Pages;

namespace QuickShell.Services.WorkspaceEditor;

internal sealed partial class WorkspaceEditor
{
    /// <summary>
    /// Applies a selected directory to the draft and populates related workspace details.
    /// </summary>
    /// <param name="directory">The directory selected for the workspace.</param>
    private void ApplyDirectorySelection(string directory)
    {
        if (!ShortcutValidation.TryNormalizeDirectory(directory, out var normalized, out _))
        {
            normalized = directory.Trim();
        }

        _draft.Directory = normalized;

        if (ShouldAutofillNameFromDirectory())
        {
            _draft.Name = DeriveNameFromDirectory(normalized);
            _autoFilledName = _draft.Name;
        }

        if (string.IsNullOrWhiteSpace(_draft.RepoUrl))
        {
            _draft.RepoUrl = GitRepoDiscovery.TryGetRemoteUrl(normalized) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_draft.DevServerUrl))
        {
            _draft.DevServerUrl = _services.ProjectAnalysis.TryDetectDevServerUrl(normalized) ?? string.Empty;
        }

        InvalidateSuggestionScan();
    }

    /// <summary>
    /// Determines whether the workspace name should be populated from the selected directory.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the current name is empty or matches the previously autofilled name, <c>false</c> otherwise.
    /// </returns>
    private bool ShouldAutofillNameFromDirectory()
    {
        if (string.IsNullOrWhiteSpace(_draft.Name))
        {
            _nameCustomized = false;
            return true;
        }

        if (_nameCustomized)
        {
            return false;
        }

        if (_autoFilledName is null)
        {
            return false;
        }

        return string.Equals(
            Normalize(_draft.Name),
            Normalize(_autoFilledName),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Derives a workspace name from the final component of a directory path.
    /// </summary>
    /// <param name="directory">The directory path from which to derive the name.</param>
    /// <returns>The directory's final component, or the trimmed directory path when no final component is available.</returns>
    private static string DeriveNameFromDirectory(string directory)
    {
        var trimmed = directory.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }
}
