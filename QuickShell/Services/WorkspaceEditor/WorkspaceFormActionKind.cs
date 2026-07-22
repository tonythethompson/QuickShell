namespace QuickShell.Services.WorkspaceEditor;

internal enum WorkspaceFormActionKind
{
    None,
    Save,
    Cancel,
    Discard,
    Browse,
    Paste,
    RefreshTerminals,
    AddSuggestedCommand,
    AddCommandRow,
    AddOpenInTerminalRow,
    RemoveLaunch,
    ExpandSuggestionPills,
    CollapseSuggestionPills,
    AddCompanionApp,
    RemoveCompanionApp,
    BrowseCompanionApp,
    ApplyCompanionPreset,
    Help,
}
