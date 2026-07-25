using System.Text.Json.Serialization;
using QuickShell.Models;
using QuickShell.Services;

namespace QuickShell;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TerminalShortcut))]
[JsonSerializable(typeof(TerminalShortcut[]))]
[JsonSerializable(typeof(List<TerminalShortcut>))]
[JsonSerializable(typeof(WorkspaceDiskRecord))]
[JsonSerializable(typeof(WorkspaceDiskRecord[]))]
[JsonSerializable(typeof(List<WorkspaceDiskRecord>))]
[JsonSerializable(typeof(WorkspaceEntry))]
[JsonSerializable(typeof(List<WorkspaceEntry>))]
[JsonSerializable(typeof(CompanionAppEntry))]
[JsonSerializable(typeof(List<CompanionAppEntry>))]
[JsonSerializable(typeof(WorkspaceSecurityMetadata))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(WorktreeBranchTargetsDocument))]
[JsonSerializable(typeof(FormChoiceJson))]
[JsonSerializable(typeof(List<FormChoiceJson>))]
[JsonSerializable(typeof(TaskTypeChoiceJson))]
[JsonSerializable(typeof(List<TaskTypeChoiceJson>))]
internal sealed partial class QuickShellJsonContext : JsonSerializerContext;
