namespace QuickShell.Services;

internal static class TerminalHostIds
{
    public const string LetWindowsChoose = "system";

    public const string WindowsTerminal = "wt";

    public const string WindowsConsoleHost = "conhost";

    public const string IntelligentTerminal = "it";

    public const string DefaultProfile = "__default__";

    public static string ResolveEffectiveApplication(string terminalApplicationId)
    {
        if (terminalApplicationId.Equals(LetWindowsChoose, StringComparison.OrdinalIgnoreCase))
        {
            return WindowsDefaultTerminalReader.ReadApplicationId();
        }

        return terminalApplicationId;
    }

    public static bool UsesWindowsTerminalProfiles(string terminalApplicationId) =>
        !ResolveEffectiveApplication(terminalApplicationId)
            .Equals(WindowsConsoleHost, StringComparison.OrdinalIgnoreCase);

    public static bool IsWindowsTerminalProfilePrefix(string idPrefix) =>
        idPrefix.Equals("wt", StringComparison.OrdinalIgnoreCase)
        || idPrefix.Equals("wtp", StringComparison.OrdinalIgnoreCase)
        || idPrefix.Equals("wtu", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedProfilePrefix(string idPrefix) =>
        IsWindowsTerminalProfilePrefix(idPrefix)
        || idPrefix.Equals(IntelligentTerminal, StringComparison.OrdinalIgnoreCase);

    public static string HostExecutable(string terminalApplicationId)
    {
        var effective = ResolveEffectiveApplication(terminalApplicationId);
        return effective.Equals(IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? "wtai.exe"
            : "wt.exe";
    }

    public static string ProfileIdPrefix(string terminalApplicationId)
    {
        var effective = ResolveEffectiveApplication(terminalApplicationId);
        return effective.Equals(IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? IntelligentTerminal
            : WindowsTerminal;
    }

    public static string SourceLabel(string terminalApplicationId)
    {
        if (terminalApplicationId.Equals(LetWindowsChoose, StringComparison.OrdinalIgnoreCase))
        {
            return "Let Windows choose";
        }

        if (terminalApplicationId.Equals(WindowsConsoleHost, StringComparison.OrdinalIgnoreCase))
        {
            return "Windows Console Host";
        }

        return ResolveEffectiveApplication(terminalApplicationId).Equals(IntelligentTerminal, StringComparison.OrdinalIgnoreCase)
            ? "Intelligent Terminal"
            : "Windows Terminal";
    }
}
