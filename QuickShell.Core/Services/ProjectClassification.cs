namespace QuickShell.Services;

[Flags]
internal enum ProjectStack
{
    None = 0,
    Node = 1 << 0,
    DotNet = 1 << 1,
    Rust = 1 << 2,
    Python = 1 << 3,
    Docker = 1 << 4,
    Monorepo = 1 << 5,
    VsCodeWorkspace = 1 << 6,
    DevContainer = 1 << 7,
    Make = 1 << 8,
    Just = 1 << 9,
    Taskfile = 1 << 10,
    Go = 1 << 11,
    Maven = 1 << 12,
    Gradle = 1 << 13,
    Deno = 1 << 14,
    Bun = 1 << 15,
    Turbo = 1 << 16,
    Nx = 1 << 17,
    Procfile = 1 << 18,
    Rails = 1 << 19,
    Elixir = 1 << 20,
}

internal sealed class ProjectClassification
{
    public static ProjectClassification Empty { get; } = new();

    public ProjectStack Stacks { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyDictionary<string, string> NodeScripts { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> DenoTasks { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> DotNetProjects { get; init; } = [];

    public IReadOnlyList<string> RunnableDotNetProjects { get; init; } = [];

    public IReadOnlyList<string> MakeTargets { get; init; } = [];

    public IReadOnlyList<string> JustRecipes { get; init; } = [];

    public IReadOnlyList<string> TaskfileTasks { get; init; } = [];

    public IReadOnlyList<VsCodeTaskSuggestion> VsCodeTasks { get; init; } = [];

    public bool HasSpringBoot { get; init; }

    public bool HasForemanRunner { get; init; }

    public bool Has(ProjectStack stack) => (Stacks & stack) == stack;
}

internal sealed record VsCodeTaskSuggestion(string Label, string Command);
