using QuickShell.Services.WorkspaceEditor;

namespace QuickShell.Core.Tests;

public sealed class WorkspaceFormActionParserTests
{
    [Fact]
    public void Parse_DataAction_ReturnsExpectedKind()
    {
        var action = WorkspaceFormActionParser.Parse("{}", """{"action":"save"}""");

        Assert.Equal(WorkspaceFormActionKind.Save, action.Kind);
    }

    [Fact]
    public void Parse_InputsFallback_WhenDataHasNoAction()
    {
        var action = WorkspaceFormActionParser.Parse("""{"action":"discard"}""", null);

        Assert.Equal(WorkspaceFormActionKind.Discard, action.Kind);
    }

    [Fact]
    public void Parse_UnknownAction_ReturnsNone()
    {
        var action = WorkspaceFormActionParser.Parse("{}", """{"action":"frobnicate"}""");

        Assert.Equal(WorkspaceFormActionKind.None, action.Kind);
    }

    [Fact]
    public void Parse_EmptyInputsAndData_ReturnsSave()
    {
        var action = WorkspaceFormActionParser.Parse("{}", "{}");

        Assert.Equal(WorkspaceFormActionKind.Save, action.Kind);
    }

    [Fact]
    public void Parse_AbsentAction_DefaultsToSave()
    {
        var action = WorkspaceFormActionParser.Parse("""{"Name":"Workspace"}""", null);

        Assert.Equal(WorkspaceFormActionKind.Save, action.Kind);
    }

    [Fact]
    public void Parse_AddSuggestedCommand_PopulatesPillData()
    {
        var action = WorkspaceFormActionParser.Parse(
            "{}",
            """{"action":"addsuggestedcommand","pillCommand":"npm test","pillTaskType":"test","pillIndex":"2"}""");

        Assert.Equal(WorkspaceFormActionKind.AddSuggestedCommand, action.Kind);
        Assert.Equal("npm test", action.PillCommand);
        Assert.Equal("test", action.PillTaskType);
        Assert.Equal(2, action.PillIndex);
    }

    [Fact]
    public void Parse_AddCommandRow_ReturnsExpectedKind()
    {
        var action = WorkspaceFormActionParser.Parse("{}", """{"action":"addcommandrow"}""");

        Assert.Equal(WorkspaceFormActionKind.AddCommandRow, action.Kind);
    }

    [Fact]
    public void Parse_AddOpenInTerminalRow_ReturnsExpectedKind()
    {
        var action = WorkspaceFormActionParser.Parse("{}", """{"action":"addopeninterminalrow"}""");

        Assert.Equal(WorkspaceFormActionKind.AddOpenInTerminalRow, action.Kind);
    }

    [Fact]
    public void Parse_RemoveLaunch_PopulatesIndex()
    {
        var action = WorkspaceFormActionParser.Parse("{}", """{"action":"removelaunch","launchIndex":"1"}""");

        Assert.Equal(WorkspaceFormActionKind.RemoveLaunch, action.Kind);
        Assert.Equal(1, action.LaunchIndex);
    }

    [Fact]
    public void Parse_RemoveCompanionApp_PopulatesIndex()
    {
        var action = WorkspaceFormActionParser.Parse("{}", """{"action":"removecompanionapp","companionIndex":"0"}""");

        Assert.Equal(WorkspaceFormActionKind.RemoveCompanionApp, action.Kind);
        Assert.Equal(0, action.CompanionIndex);
    }

    [Fact]
    public void Parse_ApplyCompanionPreset_PopulatesIndexAndPreset()
    {
        var action = WorkspaceFormActionParser.Parse(
            "{}",
            """{"action":"applycompanionpreset","companionIndex":"1","preset":"Custom"}""");

        Assert.Equal(WorkspaceFormActionKind.ApplyCompanionPreset, action.Kind);
        Assert.Equal(1, action.CompanionIndex);
        Assert.Equal("Custom", action.Preset);
    }

    [Fact]
    public void ParseDiscardPromptAction_Discard_ReturnsDiscard()
    {
        var action = WorkspaceFormActionParser.ParseDiscardPromptAction("{}", """{"action":"discard"}""");

        Assert.Equal(WorkspaceFormActionKind.Discard, action.Kind);
    }

    [Fact]
    public void ParseDiscardPromptAction_Unknown_ReturnsNone()
    {
        var action = WorkspaceFormActionParser.ParseDiscardPromptAction("{}", """{"action":"cancel"}""");

        Assert.Equal(WorkspaceFormActionKind.None, action.Kind);
    }
}
