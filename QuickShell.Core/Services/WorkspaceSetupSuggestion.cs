using QuickShell.Classification;
using QuickShell.Models;

namespace QuickShell.Services;

internal static class WorkspaceSetupSuggestion
{
    private static readonly string[] PreferredTaskNames = ["dev", "start", "test", "build"];

    public static IReadOnlyList<WorkspaceSetupTask> Build(string directory) =>
        Build(directory, ProjectAnalysisAccessor.Instance.Classify(directory));

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

        var existingLaunches = shortcut.Launches
            .OrderBy(launch => launch.Order)
            .ToArray();

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
            TaskType = GetPreservedTaskType(existingLaunches, index),
        }).ToList();
        ShortcutLaunchNormalization.MirrorLegacyFieldsFromFirstLaunch(shortcut);
    }

    private static bool HasNonemptyLaunchCommand(TerminalShortcut shortcut) =>
        shortcut.Launches.Any(launch => !string.IsNullOrWhiteSpace(launch.Command))
        || !string.IsNullOrWhiteSpace(shortcut.Command);

    private static string GetPreservedTaskType(WorkspaceEntry[] existingLaunches, int index)
    {
        if (index >= existingLaunches.Length)
        {
            return TaskTypeCatalog.None;
        }

        var taskType = TaskTypeCatalog.Normalize(existingLaunches[index].TaskType);
        return string.Equals(taskType, TaskTypeCatalog.None, StringComparison.Ordinal)
            ? TaskTypeCatalog.None
            : taskType;
    }

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
            AddRuby();
            AddElixir();
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
                    Add(ToTitle(scriptName), ProjectAnalysisAccessor.Instance.FormatPackageScriptCommand(directory, scriptName));
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

            var runnableProjects = classification.RunnableDotNetProjects
                .Where(LaunchCommandSanity.IsUsableDotNetProjectFileName)
                .ToList();

            if (runnableProjects.Count == 1)
            {
                var project = QuoteIfNeeded(runnableProjects[0]);
                Add("Watch", $"dotnet watch --project {project}");
                Add("Run", $"dotnet run --project {project}");
            }
            else if (runnableProjects.Count > 1)
            {
                Add("Watch", "dotnet watch");
            }
        }

        private void AddRust()
        {
            if (!classification.Has(ProjectStack.Rust))
            {
                return;
            }

            Add("Watch", "cargo watch -x run");
            Add("Run", "cargo run");
            Add("Tests", "cargo test");
            Add("Build", "cargo build");
        }

        private void AddPython()
        {
            if (!classification.Has(ProjectStack.Python))
            {
                return;
            }

            if (HasPythonTests())
            {
                Add("Tests", "python -m pytest");
            }

            if (File.Exists(Path.Combine(directory, "manage.py")))
            {
                Add("Run", "python manage.py runserver");
                return;
            }

            if (HasPythonDependency("flask"))
            {
                Add("Run", "flask run");
                return;
            }

            if (HasPythonDependency("uvicorn") || File.Exists(Path.Combine(directory, "main.py")))
            {
                Add("Run", "uvicorn main:app --reload");
            }
        }

        private void AddRuby()
        {
            if (!classification.Has(ProjectStack.Rails))
            {
                return;
            }

            Add("Run", File.Exists(Path.Combine(directory, "bin", "rails"))
                ? "bin/rails server"
                : "rails server");

            if (Directory.Exists(Path.Combine(directory, "test"))
                || Directory.Exists(Path.Combine(directory, "spec")))
            {
                Add("Tests", File.Exists(Path.Combine(directory, "bin", "rails"))
                    ? "bin/rails test"
                    : "rails test");
            }
        }

        private void AddElixir()
        {
            if (!classification.Has(ProjectStack.Elixir))
            {
                return;
            }

            var mixExs = Path.Combine(directory, "mix.exs");
            if (FileContains(mixExs, "phoenix"))
            {
                Add("Run", "mix phx.server");
            }

            Add("Tests", "mix test");
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
                // Skip tasks that rely on VS Code variable expansion (${workspaceFolder}, etc.).
                if (!LaunchCommandSanity.IsUsableSuggestion(task.Command))
                {
                    continue;
                }

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
            if (string.IsNullOrWhiteSpace(command)
                || !LaunchCommandSanity.IsUsableSuggestion(command)
                || !_seenCommands.Add(command))
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

        private bool HasPythonDependency(string packageName)
        {
            if (FileContains(Path.Combine(directory, "requirements.txt"), packageName))
            {
                return true;
            }

            var pyproject = Path.Combine(directory, "pyproject.toml");
            return File.Exists(pyproject) && FileContains(pyproject, packageName);
        }

        private static bool FileContains(string path, string value)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

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
