using System.Text.Json;
using System.Text.RegularExpressions;
using QuickShell.Services;

namespace QuickShell.Classification;

internal sealed partial class ProjectClassificationBuilder(string directory)
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly List<string> _labels = [];
    private ProjectStack _stacks;
    private Dictionary<string, string> _nodeScripts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _denoTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _dotNetProjects = [];
    private readonly List<string> _runnableDotNetProjects = [];
    private readonly List<string> _makeTargets = [];
    private readonly List<string> _justRecipes = [];
    private readonly List<string> _taskfileTasks = [];
    private readonly List<VsCodeTaskSuggestion> _vsCodeTasks = [];
    private bool _hasSpringBoot;
    private bool _hasForemanRunner;

    internal void TryClassifyNode() => Try(ClassifyNode);

    internal void TryClassifyDotNet() => Try(ClassifyDotNet);

    internal void TryClassifyRust() => Try(ClassifyRust);

    internal void TryClassifyPython() => Try(ClassifyPython);

    internal void TryClassifyDocker() => Try(ClassifyDocker);

    internal void TryClassifyEditors() => Try(ClassifyEditors);

    internal void TryClassifyTaskRunners() => Try(ClassifyTaskRunners);

    internal void TryClassifyGo() => Try(ClassifyGo);

    internal void TryClassifyJava() => Try(ClassifyJava);

    internal void TryClassifyDeno() => Try(ClassifyDeno);

    internal void TryClassifyProcfile() => Try(ClassifyProcfile);

    internal void TryClassifyRuby() => Try(ClassifyRuby);

    internal void TryClassifyElixir() => Try(ClassifyElixir);

    public ProjectClassification Build() =>
        new()
        {
            Stacks = _stacks,
            Labels = _labels,
            NodeScripts = _nodeScripts,
            DenoTasks = _denoTasks,
            DotNetProjects = _dotNetProjects,
            RunnableDotNetProjects = _runnableDotNetProjects,
            MakeTargets = _makeTargets,
            JustRecipes = _justRecipes,
            TaskfileTasks = _taskfileTasks,
            VsCodeTasks = _vsCodeTasks,
            HasSpringBoot = _hasSpringBoot,
            HasForemanRunner = _hasForemanRunner,
        };

    private void ClassifyNode()
    {
        var packageJsonPath = Path.Combine(directory, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return;
        }

        Add(ProjectStack.Node, "Node");
        using var document = TryParseJson(packageJsonPath);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        _nodeScripts = ReadStringObject(document.RootElement, "scripts");
        if (document.RootElement.TryGetProperty("workspaces", out _)
            || File.Exists(Path.Combine(directory, "pnpm-workspace.yaml"))
            || File.Exists(Path.Combine(directory, "pnpm-workspace.yml")))
        {
            Add(ProjectStack.Monorepo, "monorepo");
        }

        if (HasDependency(document.RootElement, "turbo") || HasScriptValueContaining(_nodeScripts, "turbo"))
        {
            Add(ProjectStack.Turbo, "Turbo");
            Add(ProjectStack.Monorepo, "monorepo");
        }

        if (HasDependency(document.RootElement, "nx") || HasScriptValueContaining(_nodeScripts, "nx"))
        {
            Add(ProjectStack.Nx, "Nx");
            Add(ProjectStack.Monorepo, "monorepo");
        }

        if (File.Exists(Path.Combine(directory, "bun.lockb")) || File.Exists(Path.Combine(directory, "bun.lock")))
        {
            Add(ProjectStack.Bun, "Bun");
        }
    }

    private void ClassifyDotNet()
    {
        var projectFiles = Directory
            .EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path =>
                path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (projectFiles.Count == 0)
        {
            return;
        }

        Add(ProjectStack.DotNet, ".NET");
        foreach (var project in projectFiles.Where(path =>
                     path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                     || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)))
        {
            _dotNetProjects.Add(Path.GetFileName(project));
            if (IsRunnableDotNetProject(project))
            {
                _runnableDotNetProjects.Add(Path.GetFileName(project));
            }
        }
    }

    private void ClassifyRust()
    {
        if (File.Exists(Path.Combine(directory, "Cargo.toml")))
        {
            Add(ProjectStack.Rust, "Rust");
        }
    }

    private void ClassifyPython()
    {
        if (File.Exists(Path.Combine(directory, "pyproject.toml"))
            || File.Exists(Path.Combine(directory, "requirements.txt"))
            || File.Exists(Path.Combine(directory, "setup.py")))
        {
            Add(ProjectStack.Python, "Python");
        }
    }

    private void ClassifyDocker()
    {
        if (File.Exists(Path.Combine(directory, "docker-compose.yml"))
            || File.Exists(Path.Combine(directory, "docker-compose.yaml"))
            || File.Exists(Path.Combine(directory, "compose.yml"))
            || File.Exists(Path.Combine(directory, "compose.yaml")))
        {
            Add(ProjectStack.Docker, "Docker");
        }
    }

    private void ClassifyEditors()
    {
        if (Directory.Exists(Path.Combine(directory, ".vscode"))
            || Directory.EnumerateFiles(directory, "*.code-workspace", SearchOption.TopDirectoryOnly).Any())
        {
            Add(ProjectStack.VsCodeWorkspace, "VS Code");
            _vsCodeTasks.AddRange(ReadVsCodeTasks());
        }

        if (Directory.Exists(Path.Combine(directory, ".devcontainer"))
            || File.Exists(Path.Combine(directory, "devcontainer.json")))
        {
            Add(ProjectStack.DevContainer, "dev container");
        }
    }

    private void ClassifyTaskRunners()
    {
        if (File.Exists(Path.Combine(directory, "Makefile")) || File.Exists(Path.Combine(directory, "makefile")))
        {
            Add(ProjectStack.Make, "Make");
            _makeTargets.AddRange(ReadMakeTargets());
        }

        if (File.Exists(Path.Combine(directory, "justfile")) || File.Exists(Path.Combine(directory, "Justfile")))
        {
            Add(ProjectStack.Just, "just");
            _justRecipes.AddRange(ReadJustRecipes());
        }

        var taskfile = FindFirst("Taskfile.yml", "Taskfile.yaml");
        if (taskfile is not null)
        {
            Add(ProjectStack.Taskfile, "Taskfile");
            _taskfileTasks.AddRange(ReadTaskfileTasks(taskfile));
        }
    }

    private void ClassifyGo()
    {
        if (File.Exists(Path.Combine(directory, "go.mod")))
        {
            Add(ProjectStack.Go, "Go");
        }
    }

    private void ClassifyJava()
    {
        var pom = Path.Combine(directory, "pom.xml");
        if (File.Exists(pom))
        {
            Add(ProjectStack.Maven, "Maven");
            _hasSpringBoot |= FileContains(pom, "spring-boot-maven-plugin");
        }

        var gradleFiles = new[]
        {
            Path.Combine(directory, "build.gradle"),
            Path.Combine(directory, "build.gradle.kts"),
        }.Where(File.Exists).ToList();

        if (gradleFiles.Count > 0)
        {
            Add(ProjectStack.Gradle, "Gradle");
            _hasSpringBoot |= gradleFiles.Any(path => FileContains(path, "org.springframework.boot")
                || FileContains(path, "bootRun"));
        }
    }

    private void ClassifyDeno()
    {
        var denoPath = FindFirst("deno.json", "deno.jsonc");
        if (denoPath is null)
        {
            return;
        }

        Add(ProjectStack.Deno, "Deno");
        using var document = TryParseJson(denoPath);
        if (document is not null && document.RootElement.ValueKind == JsonValueKind.Object)
        {
            _denoTasks = ReadStringObject(document.RootElement, "tasks");
        }
    }

    private void ClassifyProcfile()
    {
        var procfile = Path.Combine(directory, "Procfile");
        if (!File.Exists(procfile))
        {
            return;
        }

        Add(ProjectStack.Procfile, "Procfile");
        _hasForemanRunner = FileContains(procfile, "foreman")
            || FileContains(procfile, "overmind")
            || FileContains(Path.Combine(directory, "Gemfile"), "foreman")
            || FileContains(Path.Combine(directory, "package.json"), "foreman")
            || FileContains(Path.Combine(directory, "package.json"), "overmind");
    }

    private void ClassifyRuby()
    {
        var gemfile = Path.Combine(directory, "Gemfile");
        if (File.Exists(gemfile) && FileContains(gemfile, "rails"))
        {
            Add(ProjectStack.Rails, "Rails");
        }
    }

    private void ClassifyElixir()
    {
        var mixExs = Path.Combine(directory, "mix.exs");
        if (File.Exists(mixExs))
        {
            Add(ProjectStack.Elixir, "Elixir");
        }
    }

    private string? FindFirst(params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private List<VsCodeTaskSuggestion> ReadVsCodeTasks()
    {
        var tasksPath = Path.Combine(directory, ".vscode", "tasks.json");
        if (!File.Exists(tasksPath))
        {
            return [];
        }

        using var document = TryParseJson(tasksPath);
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var suggestions = new List<VsCodeTaskSuggestion>();
        foreach (var task in tasks.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object
                || task.TryGetProperty("dependsOn", out _)
                || !TryReadString(task, "command", out var command))
            {
                continue;
            }

            var type = TryReadString(task, "type", out var taskType) ? taskType : "shell";
            if (!type.Equals("shell", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("process", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var label = TryReadString(task, "label", out var taskLabel)
                ? taskLabel
                : command;
            var args = ReadStringArray(task, "args");
            suggestions.Add(new VsCodeTaskSuggestion(label, JoinCommand(command, args)));
        }

        return suggestions;
    }

    private List<string> ReadMakeTargets()
    {
        var path = File.Exists(Path.Combine(directory, "Makefile"))
            ? Path.Combine(directory, "Makefile")
            : Path.Combine(directory, "makefile");
        return ReadRegexMatches(path, MakeTargetRegex());
    }

    private List<string> ReadJustRecipes()
    {
        var path = File.Exists(Path.Combine(directory, "justfile"))
            ? Path.Combine(directory, "justfile")
            : Path.Combine(directory, "Justfile");
        return ReadRegexMatches(path, JustRecipeRegex());
    }

    private static List<string> ReadTaskfileTasks(string path) =>
        ReadRegexMatches(path, TaskfileTaskRegex());

    private static List<string> ReadRegexMatches(string path, Regex regex)
    {
        try
        {
            return File
                .ReadLines(path)
                .Select(line => regex.Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value)
                .Where(name => name.Length == 0 || name[0] != '.')
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsRunnableDotNetProject(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return File.ReadLines(path).Any(line =>
                line.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
                || line.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static JsonDocument? TryParseJson(string path)
    {
        try
        {
            return JsonFileDocument.Parse(path, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadStringObject(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in section.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.Value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[property.Name] = value;
            }
        }

        return values;
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static List<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var args))
        {
            return [];
        }

        if (args.ValueKind == JsonValueKind.String)
        {
            var value = args.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];
        }

        if (args.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return args
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static string JoinCommand(string command, List<string> args)
    {
        if (args.Count == 0)
        {
            return command;
        }

        return $"{command} {string.Join(" ", args.Select(QuoteIfNeeded))}";
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static bool HasDependency(JsonElement root, string packageName)
    {
        foreach (var propertyName in new[] { "dependencies", "devDependencies", "peerDependencies" })
        {
            if (!root.TryGetProperty(propertyName, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (section.TryGetProperty(packageName, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasScriptValueContaining(Dictionary<string, string> scripts, string value) =>
        scripts.Values.Any(script => script.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool FileContains(string path, string value)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return File.ReadLines(path).Any(line => line.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void Add(ProjectStack stack, string label)
    {
        _stacks |= stack;
        if (!_labels.Contains(label, StringComparer.OrdinalIgnoreCase))
        {
            _labels.Add(label);
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Repository discovery should degrade to fewer suggestions, not fail.
        }
    }

    [GeneratedRegex(@"^([A-Za-z0-9_.-]+)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex MakeTargetRegex();

    [GeneratedRegex(@"^([A-Za-z0-9_-]+)\s*(?:[^:=]*)?:", RegexOptions.CultureInvariant)]
    private static partial Regex JustRecipeRegex();

    [GeneratedRegex(@"^\s{2}([A-Za-z0-9_.-]+)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex TaskfileTaskRegex();
}
