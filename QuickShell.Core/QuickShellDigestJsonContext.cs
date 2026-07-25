using System.Text.Json.Serialization;
using QuickShell.Models;

namespace QuickShell;

/// <summary>
/// Source-generated serializer for <see cref="WorkspaceSecurityPolicy.ComputeDigest"/> only.
/// Options must match System.Text.Json's default <c>Serialize(object)</c> output (compact,
/// nulls included, PascalCase) so existing <c>WorkspaceReviewToken</c> digests keep matching.
/// Do not reuse <see cref="QuickShellJsonContext"/> here: that context uses WriteIndented and
/// WhenWritingNull for settings/form payloads and would invalidate outstanding trust tokens.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(WorkspaceDigestPayload))]
[JsonSerializable(typeof(WorkspaceEntry))]
[JsonSerializable(typeof(List<WorkspaceEntry>))]
[JsonSerializable(typeof(CompanionAppEntry))]
[JsonSerializable(typeof(List<CompanionAppEntry>))]
internal sealed partial class QuickShellDigestJsonContext : JsonSerializerContext;
