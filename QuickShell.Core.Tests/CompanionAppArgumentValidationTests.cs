using QuickShell.Services;

namespace QuickShell.Core.Tests;

public sealed class CompanionAppArgumentValidationTests
{
    [Fact]
    public void ShouldShowArgumentsField_HiddenForNoneAndCustomWithoutPath()
    {
        Assert.False(CompanionAppArgumentValidation.ShouldShowArgumentsField(
            CompanionAppCatalog.PresetNone,
            path: null));
        Assert.False(CompanionAppArgumentValidation.ShouldShowArgumentsField(
            CompanionAppCatalog.PresetCustom,
            path: null));
    }

    [Fact]
    public void ShouldShowArgumentsField_VisibleForCustomWithPath()
    {
        Assert.True(CompanionAppArgumentValidation.ShouldShowArgumentsField(
            CompanionAppCatalog.PresetCustom,
            @"C:\Apps\Code.exe"));
    }

    [Fact]
    public void NormalizeForSave_EmptyUsesPresetDefault()
    {
        Assert.Equal(".", CompanionAppArgumentValidation.NormalizeForSave(
            CompanionAppCatalog.PresetVsCode,
            @"C:\Apps\Code.exe",
            arguments: null));
        Assert.Equal("{solution}", CompanionAppArgumentValidation.NormalizeForSave(
            CompanionAppCatalog.PresetVs2022,
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
            arguments: "   "));
    }

    [Fact]
    public void TryValidateForSave_RejectsUnknownPlaceholder()
    {
        Assert.False(CompanionAppArgumentValidation.TryValidateForSave(
            CompanionAppCatalog.PresetVsCode,
            @"C:\Apps\Code.exe",
            "{workspace}",
            out var error));
        Assert.Contains("folder", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildArgumentWarning_SuggestsSolutionForVisualStudio()
    {
        var root = Path.Combine(Path.GetTempPath(), "quickshell-args-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);

        try
        {
            var warning = CompanionAppArgumentValidation.BuildArgumentWarning(
                CompanionAppCatalog.PresetVs2022,
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
                ".",
                root);

            Assert.NotNull(warning);
            Assert.Contains("{solution}", warning, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void BuildArgumentWarning_SuggestsDotForEditorPresets()
    {
        var warning = CompanionAppArgumentValidation.BuildArgumentWarning(
            CompanionAppCatalog.PresetCursor,
            @"C:\Apps\Cursor.exe",
            "--new-window",
            @"C:\Projects\demo");

        Assert.NotNull(warning);
        Assert.Contains(".", warning, StringComparison.Ordinal);
    }
}
