using System.Text.Json;
using QuickShell.Models;

namespace QuickShell.Services;

/// <summary>
/// Kill switch for workspace-trust enforcement and host trust UI.
/// Ship default comes from <c>shared/workspace-trust-features.json</c> (embedded;
/// Raycast copies the same file). Flip that JSON when shipping trust for real.
/// </summary>
internal static class WorkspaceTrustFeatures
{
    private static readonly object Gate = new();
    private static int _overrideDepth;
    private static bool _overrideEnabled;

    /// <summary>
    /// Ship-time default from the shared JSON. Tests may temporarily override via
    /// <see cref="EnableForTests"/>; the override is nestable and process-gated.
    /// </summary>
    public static bool DefaultEnabled { get; } = ReadDefaultEnabled();

    /// <summary>
    /// When false, untrusted metadata does not block external actions and hosts
    /// should hide Trust / Revoke / Untrusted chrome.
    /// </summary>
    public static bool Enabled
    {
        get
        {
            lock (Gate)
            {
                return _overrideDepth > 0 ? _overrideEnabled : DefaultEnabled;
            }
        }
    }

    /// <summary>
    /// Security metadata for imported / restored / otherwise external ingress.
    /// Trusted while enforcement is off so re-enabling later does not suddenly
    /// lock users out of workspaces they already used.
    /// </summary>
    public static WorkspaceSecurityMetadata CreateIngressSecurity() =>
        new()
        {
            IsTrusted = !Enabled,
            Revision = 1,
        };

    /// <summary>
    /// While enforcement is off, rewrite untrusted local rows to trusted so a later
    /// re-enable does not revive stale denials from a prior trust-on window.
    /// </summary>
    public static bool CoerceTrustedWhileDisabled(IList<ShortcutLayoutEntry> layout)
    {
        if (Enabled)
        {
            return false;
        }

        var changed = false;
        foreach (var entry in layout)
        {
            if (entry.Kind != ShortcutLayoutEntryKind.Shortcut)
            {
                continue;
            }

            var security = entry.Security ?? new WorkspaceSecurityMetadata();
            if (security.IsTrusted)
            {
                continue;
            }

            entry.Security = security with { IsTrusted = true };
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Enables enforcement for the duration of a test, then restores the prior override stack.
    /// Callers must run in a non-parallel collection (e.g. ShortcutRepositoryMutex).
    /// </summary>
    public static IDisposable EnableForTests() => new EnabledScope(enabled: true);

    /// <summary>
    /// Forces the kill switch off for a test even if <see cref="DefaultEnabled"/> is true.
    /// </summary>
    public static IDisposable DisableForTests() => new EnabledScope(enabled: false);

    private static bool ReadDefaultEnabled()
    {
        const string resourceName = "QuickShell.workspace-trust-features.json";
        var assembly = typeof(WorkspaceTrustFeatures).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("enabled", out var enabled)
            || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException($"Resource '{resourceName}' must contain a boolean 'enabled' property.");
        }

        return enabled.GetBoolean();
    }

    private sealed class EnabledScope : IDisposable
    {
        private bool _disposed;

        public EnabledScope(bool enabled)
        {
            lock (Gate)
            {
                if (_overrideDepth == 0)
                {
                    _overrideEnabled = enabled;
                }
                else if (_overrideEnabled != enabled)
                {
                    throw new InvalidOperationException(
                        "Nested WorkspaceTrustFeatures test overrides must agree on the Enabled value.");
                }

                _overrideDepth++;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (Gate)
            {
                if (_overrideDepth > 0)
                {
                    _overrideDepth--;
                }
            }
        }
    }
}
