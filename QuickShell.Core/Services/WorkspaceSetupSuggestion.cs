using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceSetupSuggestion
{
    private static readonly string[] PreferredTaskNames = ["dev", "start", "test", "build"];

    public static IReadOnlyList<WorkspaceSetupTask> Build(string directory) =>
        Build(directory, ProjectClassifier.Classify(directory));

    public static IReadOnlyList<WorkspaceSetupTask> Build(string directory, ProjectClassification classification)
    {
        if (string.IsNullOrWhiteSpace(directory) || classification.Stacks == ProjectStack.None)
        {
            return [];
        }

        var builder = new Builder(directory, classification);
        builder.AddSuggestions();
        return builder.Tasks;
    }

    public static string? TryGetPrimaryCommand(string directory)
    {
        var suggestions = Build(directory);
        return suggestions.Count == 0 ? null : suggestions[0].Command;
    }

    public static void ApplyToShortcut(TerminalShortcut shortcut, ProjectClassification classification)
    {
        if (HasNonemptyLaunchCommand(shortcut))
        {
            return;
        }

        var suggestions = Build(shortcut.Directory, classification);
        if (suggestions.Count == 0)
        {
            return;
        }

        shortcut.Launches = suggestions.Select((suggestion, index) => new WorkspaceEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = suggestion.Label,
            Command = suggestion.Command,
            Terminal = string.IsNullOrWhiteSpace(shortcut.Terminal) ? "default" : shortcut.Terminal,
            WtProfile = shortcut.WtProfile,
            RunAsAdmin = shortcut.RunAsAdmin,
            IsEnabled = true,
            Order = index,
        }).ToList();
        ShortcutLaunchNormalization.MirrorLegacyFieldsFromFirstLaunch(shortcut);
    }

    private static bool HasNonemptyLaunchCommand(TerminalShortcut shortcut) =>
        shortcut.Launches.Any(launch => !string.IsNullOrWhiteSpace(launch.Command))
        || !string.IsNullOrWhiteSpace(shortcut.Command);

    private sealed class Builder(string directory, ProjectClassification classification)
    {
        private readonly List<WorkspaceSetupTask> _tasks = [];
        private readonly HashSet<string> _seenCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenLabels = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<WorkspaceSetupTask> Tasks => _tasks;

        public void AddSuggestions()
        {
            AddNode();
            AddDotNet();
            AddRust();
            AddPython();
            AddDocker();
            AddMake();
            AddJust();
            AddTaskfile();
            AddGo();
            AddMaven();
            AddGradle();
            AddDeno();
            AddVsCodeTasks();
            AddProcfile();
        }

        private void AddNode()
        {
            if (!classification.Has(ProjectStack.Node))
            {
                return;
            }

            foreach (var scriptName in PreferredTaskNames)
            {
                if (classification.NodeScripts.ContainsKey(scriptName))
                {
                    Add(ToTitle(scriptName), DevServerUrlDetection.FormatPackageScriptCommand(directory, scriptName));
                }
            }
        }

        private void AddDotNet()
        {
            if (!classification.Has(ProjectStack.DotNet))
            {
                return;
            }

            Add("Build", "dotnet build");
            Add("Tests", "dotnet test");

            if (classification.RunnableDotNetProjects.Count == 1)
            {
                Add("Run", $"dotnet run --project {QuoteIfNeeded(classification.RunnableDotNetProjects[0])}");
            }
        }

        private void AddRust()
        {
            if (!classification.Has(ProjectStack.Rust))
            {
                return;
            }

            Add("Run", "cargo run");
            Add("Tests", "cargo test");
            Add("Build", "cargo build");
        }

        private void AddPython()
        {
            if (classification.Has(ProjectStack.Python) && HasPythonTests())
            {
                Add("Tests", "python -m pytest");
            }
        }

        private void AddDocker()
        {
            if (!classification.Has(ProjectStack.Docker))
            {
                return;
            }

            Add("Docker up", "docker compose up");
            Add("Docker logs", "docker compose logs -f");
        }

        private void AddMake()
        {
            if (!classification.Has(ProjectStack.Make))
            {
                return;
            }

            if (classification.MakeTargets.Count > 0)
            {
                Add("Make", "make");
            }

            foreach (var target in PreferredTaskNames.Where(classification.MakeTargets.Contains))
            {
                Add($"Make {target}", $"make {target}");
            }
        }

        private void AddJust()
        {
            if (!classification.Has(ProjectStack.Just))
            {
                return;
            }

            foreach (var recipe in PreferredTaskNames.Where(classification.JustRecipes.Contains))
            {
                Add($"just {recipe}", $"just {recipe}");
            }
        }

        private void AddTaskfile()
        {
            if (!classification.Has(ProjectStack.Taskfile))
            {
                return;
            }

            foreach (var task in PreferredTaskNames.Where(classification.TaskfileTasks.Contains))
            {
                Add($"task {task}", $"task {task}");
            }
        }

        private void AddGo()
        {
            if (!classification.Has(ProjectStack.Go))
            {
                return;
            }

            Add("Run", "go run .");
            Add("Tests", "go test ./...");
            Add("Build", "go build ./...");
        }

        private void AddMaven()
        {
            if (!classification.Has(ProjectStack.Maven))
            {
                return;
            }

            if (classification.HasSpringBoot)
            {
                Add("Run", "mvn spring-boot:run");
            }

            Add("Tests", "mvn test");
            Add("Build", "mvn package");
        }

        private void AddGradle()
        {
            if (!classification.Has(ProjectStack.Gradle))
            {
                return;
            }

            var gradle = File.Exists(Path.Combine(directory, "gradlew"))
                || File.Exists(Path.Combine(directory, "gradlew.bat"))
                    ? ".\\gradlew"
                    : "gradle";

            if (classification.HasSpringBoot)
            {
                Add("Run", $"{gradle} bootRun");
            }

            Add("Tests", $"{gradle} test");
            Add("Build", $"{gradle} build");
        }

        private void AddDeno()
        {
            if (!classification.Has(ProjectStack.Deno))
            {
                return;
            }

            foreach (var task in PreferredTaskNames.Where(classification.DenoTasks.ContainsKey))
            {
                Add(ToTitle(task), $"deno task {task}");
            }
        }

        private void AddVsCodeTasks()
        {
            foreach (var task in classification.VsCodeTasks)
            {
                Add($"VS Code: {task.Label}", task.Command);
            }
        }

        private void AddProcfile()
        {
            if (classification.Has(ProjectStack.Procfile) && classification.HasForemanRunner)
            {
                Add("Procfile", "foreman start");
            }
        }

        private void Add(string label, string command)
        {
            if (string.IsNullOrWhiteSpace(command) || !_seenCommands.Add(command))
            {
                return;
            }

            var uniqueLabel = DeduplicateLabel(label);
            _tasks.Add(new WorkspaceSetupTask(uniqueLabel, command));
        }

        private string DeduplicateLabel(string label)
        {
            var trimmed = string.IsNullOrWhiteSpace(label) ? "Task" : label.Trim();
            if (_seenLabels.Add(trimmed))
            {
                return trimmed;
            }

            for (var i = 2; i < 100; i++)
            {
                var candidate = $"{trimmed} {i}";
                if (_seenLabels.Add(candidate))
                {
                    return candidate;
                }
            }

            return Guid.NewGuid().ToString("N");
        }

        private bool HasPythonTests() =>
            Directory.Exists(Path.Combine(directory, "tests"))
            || Directory.Exists(Path.Combine(directory, "test"))
            || Directory.EnumerateFiles(directory, "test_*.py", SearchOption.TopDirectoryOnly).Any()
            || File.Exists(Path.Combine(directory, "pytest.ini"))
            || File.Exists(Path.Combine(directory, "tox.ini"));

        private static string ToTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Task";
            }

            return value.Equals("test", StringComparison.OrdinalIgnoreCase)
                ? "Tests"
                : string.Concat(value[..1].ToUpperInvariant(), value.AsSpan(1));
        }

        private static string QuoteIfNeeded(string value) =>
            value.Any(char.IsWhiteSpace)
                ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                : value;
    }
}

internal sealed record WorkspaceSetupTask(string Label, string Command);
