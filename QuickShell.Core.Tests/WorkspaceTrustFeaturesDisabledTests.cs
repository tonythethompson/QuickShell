using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell.Core.Tests;

/// <summary>
/// Documents the ship-time kill switch: enforcement off means untrusted
/// metadata does not block external actions (review UX deferred).
/// </summary>
[Collection("ShortcutRepositoryMutex")]
public sealed class WorkspaceTrustFeaturesDisabledTests
{
    [Fact]
    public void When_disabled_untrusted_workspace_allows_all_trust_gated_actions()
    {
        using var scope = WorkspaceTrustFeatures.DisableForTests();
        var folder = Path.Join(Path.GetTempPath(), "QuickShellTrustDisabled", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var content = CreateWorkspace(folder);
            content.DevServerUrl = "https://localhost:3000/";
            content.CompanionApps =
            [
                new CompanionAppEntry
                {
                    Id = "companion-1",
                    Path = Path.Join(Environment.SystemDirectory, "cmd.exe"),
                    OpenOnLaunch = true,
                },
            ];
            var workspace = new StoredWorkspace(
                content,
                new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 1 },
                1);
            var launchEntry = content.Launches[0];

            Assert.True(WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.LaunchTerminal).IsAllowed);
            Assert.True(WorkspaceSecurityPolicy.AuthorizeLaunchEntry(workspace, launchEntry).IsAllowed);
            Assert.True(WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDirectory).IsAllowed);
            Assert.True(WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenUrl).IsAllowed);
            Assert.True(WorkspaceSecurityPolicy.Authorize(workspace, WorkspaceAction.OpenDevServer).IsAllowed);
            Assert.True(WorkspaceSecurityPolicy.AuthorizeCompanion(workspace, content.CompanionApps[0]).IsAllowed);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void Ingress_security_is_trusted_while_disabled()
    {
        using var scope = WorkspaceTrustFeatures.DisableForTests();
        Assert.True(WorkspaceTrustFeatures.CreateIngressSecurity().IsTrusted);
    }

    [Fact]
    public void Coerce_trusted_while_disabled_rewrites_untrusted_rows()
    {
        using var scope = WorkspaceTrustFeatures.DisableForTests();
        var layout = new List<ShortcutLayoutEntry>
        {
            ShortcutLayoutEntry.FromShortcut(
                CreateWorkspace(Path.GetTempPath()),
                new WorkspaceSecurityMetadata { IsTrusted = false, Revision = 3 }),
            ShortcutLayoutEntry.FromSeparator("Section"),
        };

        Assert.True(WorkspaceTrustFeatures.CoerceTrustedWhileDisabled(layout));
        Assert.True(layout[0].Security!.IsTrusted);
        Assert.Equal(3, layout[0].Security!.Revision);
        Assert.False(WorkspaceTrustFeatures.CoerceTrustedWhileDisabled(layout));
    }

    [Fact]
    public void Repository_load_coerces_untrusted_rows_while_disabled()
    {
        var folder = Path.Join(Path.GetTempPath(), "QuickShellTrustCoerce", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string configDirectory;
            using (WorkspaceTrustFeatures.EnableForTests())
            using (var seed = new ShortcutRepository(folder))
            {
                seed.Upsert(CreateWorkspace(folder));
                var id = seed.GetByName("DisabledTrust")!.Id;
                Assert.Equal(TrustTransitionStatus.Revoked, seed.RevokeTrust(id).Status);
                Assert.False(seed.GetStoredWorkspace(id)!.Security.IsTrusted);
                configDirectory = seed.ConfigDirectory;
            }

            using (WorkspaceTrustFeatures.DisableForTests())
            using (var reloaded = new ShortcutRepository(configDirectory))
            {
                var stored = reloaded.GetStoredWorkspace(reloaded.GetByName("DisabledTrust")!.Id);
                Assert.NotNull(stored);
                Assert.True(stored.Security.IsTrusted);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void Shared_json_default_matches_embedded_default()
    {
        var sharedPath = FindSharedTrustFeaturesPath();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sharedPath));
        var enabled = document.RootElement.GetProperty("enabled").GetBoolean();
        Assert.Equal(enabled, WorkspaceTrustFeatures.DefaultEnabled);
    }

    private static string FindSharedTrustFeaturesPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Join(directory.FullName, "shared", "workspace-trust-features.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate shared/workspace-trust-features.json from the test base directory.");
    }

    private static TerminalShortcut CreateWorkspace(string folder) =>
        new()
        {
            Id = "ws-disabled",
            Name = "DisabledTrust",
            Directory = folder,
            Command = "echo hi",
            Launches =
            [
                new WorkspaceEntry
                {
                    Id = "launch-1",
                    Label = "Launch",
                    Command = "echo hi",
                    IsEnabled = true,
                },
            ],
        };
}
