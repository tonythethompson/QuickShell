namespace QuickShell.Services;

/// <summary>
/// Adaptive Card template + data JSON pair for a form surface.
/// </summary>
internal readonly record struct ShortcutFormCard(string TemplateJson, string DataJson);
