using QuickShell.Classification;

namespace QuickShell.Services;

internal static class TaskTypeCandidateBuilder
{
    public static IReadOnlyList<TaskTypeCandidate> Build(
        string taskType,
        SuggestionContext context,
        TaskTypePickContext pickContext)
    {
        var normalized = TaskTypeCatalog.Normalize(taskType);
        if (normalized == TaskTypeCatalog.None)
        {
            return [];
        }

        var candidates = new List<TaskTypeCandidate>();
        var seenCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in context.Suggestions)
        {
            AddCandidate(candidates, seenCommands, suggestion.Label, suggestion.Command, "workspace", ScoreSuggestion(normalized, suggestion, context));
        }

        foreach (var suggestion in DockerComposeDiscovery.BuildServiceSuggestions(context.Directory)
                     .Take(CommandSuggestionService.MaxDockerServices * 2))
        {
            AddCandidate(
                candidates,
                seenCommands,
                suggestion.Label,
                suggestion.Command,
                "docker-service",
                ScoreSuggestion(normalized, suggestion, context));
        }

        if (context.Classification.Has(ProjectStack.Node))
        {
            foreach (var (scriptName, scriptValue) in context.Classification.NodeScripts
                         .Take(CommandSuggestionService.MaxNodeScripts))
            {
                var command = ProjectAnalysisAccessor.Instance.FormatPackageScriptCommand(context.Directory, scriptName);
                AddCandidate(
                    candidates,
                    seenCommands,
                    scriptName,
                    command,
                    "node-script",
                    ScoreNodeScript(normalized, scriptName, scriptValue, command, context));
            }
        }

        if (context.Classification.Has(ProjectStack.Deno))
        {
            foreach (var (taskName, taskValue) in context.Classification.DenoTasks
                         .Take(CommandSuggestionService.MaxNodeScripts))
            {
                var command = $"deno task {taskName}";
                AddCandidate(
                    candidates,
                    seenCommands,
                    taskName,
                    command,
                    "deno-task",
                    ScoreNodeScript(normalized, taskName, taskValue, command, context));
            }
        }

        return candidates
            .Where(candidate => candidate.Score > 0)
            .Where(candidate => !pickContext.UsedCommands.Contains(candidate.Command))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Command, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddCandidate(
        List<TaskTypeCandidate> candidates,
        HashSet<string> seenCommands,
        string label,
        string command,
        string source,
        int score)
    {
        if (score <= 0 || string.IsNullOrWhiteSpace(command) || !seenCommands.Add(command))
        {
            return;
        }

        candidates.Add(new TaskTypeCandidate(command, label, score, source));
    }

    private static int ScoreSuggestion(string taskType, WorkspaceSetupTask suggestion, SuggestionContext context)
    {
        return taskType switch
        {
            TaskTypeCatalog.Test => ScoreTest(suggestion),
            TaskTypeCatalog.Build => ScoreBuild(suggestion),
            TaskTypeCatalog.Services => ScoreServices(suggestion, context),
            TaskTypeCatalog.Api => ScoreApi(suggestion, context),
            TaskTypeCatalog.Frontend => ScoreFrontend(suggestion, context),
            TaskTypeCatalog.Logs => ScoreLogs(suggestion, context),
            _ => 0,
        };
    }

    private static int ScoreNodeScript(
        string taskType,
        string scriptName,
        string scriptValue,
        string command,
        SuggestionContext context)
    {
        var normalizedScript = scriptName.Trim().ToLowerInvariant();
        var suggestion = new WorkspaceSetupTask(scriptName, command);
        var baseScore = ScoreSuggestion(taskType, suggestion, context);
        var monorepoBonus = MonorepoFilterBonus(taskType, scriptValue);
        if (baseScore > 0)
        {
            return baseScore + ScriptNameBonus(taskType, normalizedScript) + monorepoBonus;
        }

        return taskType switch
        {
            TaskTypeCatalog.Test when IsTestScriptName(normalizedScript) => 55 + ScriptNameBonus(taskType, normalizedScript) + monorepoBonus,
            TaskTypeCatalog.Build when IsBuildScriptName(normalizedScript) => 55 + ScriptNameBonus(taskType, normalizedScript) + monorepoBonus,
            TaskTypeCatalog.Api when IsApiScriptName(normalizedScript, scriptValue) => 58 + ScriptNameBonus(taskType, normalizedScript) + monorepoBonus,
            TaskTypeCatalog.Frontend when IsFrontendScriptName(normalizedScript, scriptValue) => 58 + ScriptNameBonus(taskType, normalizedScript) + monorepoBonus,
            TaskTypeCatalog.Logs when IsLogsScriptName(normalizedScript, scriptValue) => 52 + ScriptNameBonus(taskType, normalizedScript) + monorepoBonus,
            _ => 0,
        };
    }

    private static int ScriptNameBonus(string taskType, string scriptName)
    {
        if (scriptName.Contains(':', StringComparison.Ordinal))
        {
            return 8;
        }

        return string.Equals(scriptName, taskType, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
    }

    private static int ScoreTest(WorkspaceSetupTask suggestion)
    {
        var label = NormalizeSuggestionLabel(suggestion.Label);
        var score = 0;
        if (label.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (ContainsAny(suggestion.Command, "dotnet test", "cargo test", "go test", "pytest", "mvn test", " gradle test", "jest", "vitest", "npm test", "pnpm test", "yarn test", "bun test", "deno test", "mix test", "rails test", "bin/rails test"))
        {
            score += 45;
        }

        if (suggestion.Command.Contains(" run test", StringComparison.OrdinalIgnoreCase)
            || suggestion.Command.Contains(" test", StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        return score;
    }

    private static int ScoreBuild(WorkspaceSetupTask suggestion)
    {
        var score = 0;
        if (suggestion.Label.Equals("Build", StringComparison.OrdinalIgnoreCase)
            || suggestion.Label.StartsWith("Make build", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (ContainsAny(suggestion.Command, "dotnet build", "cargo build", "go build", "mvn package", "gradle build", "make build", " run build", "npm run build", "pnpm build", "yarn build", "bun run build"))
        {
            score += 45;
        }

        if (suggestion.Label.Equals("Make", StringComparison.OrdinalIgnoreCase)
            || suggestion.Command.Equals("make", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }

    private static int ScoreServices(WorkspaceSetupTask suggestion, SuggestionContext context)
    {
        var score = 0;
        if (suggestion.Command.Contains("compose up", StringComparison.OrdinalIgnoreCase)
            || suggestion.Command.Contains("docker up", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (TryGetDockerServiceName(suggestion, out var serviceName))
        {
            score += DockerComposeDiscovery.ClassifyService(serviceName) switch
            {
                DockerServiceRole.Services => 65,
                DockerServiceRole.Unknown => 25,
                _ => 0,
            };
        }
        else if (suggestion.Label.Contains("Docker up", StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        if (suggestion.Command.Contains("logs", StringComparison.OrdinalIgnoreCase))
        {
            score -= 40;
        }

        return score;
    }

    private static int ScoreApi(WorkspaceSetupTask suggestion, SuggestionContext context)
    {
        if (IsBlockedApiSuggestion(suggestion, context))
        {
            return 0;
        }

        var label = NormalizeSuggestionLabel(suggestion.Label);
        var score = 0;
        if (suggestion.Command.Contains("dotnet watch", StringComparison.OrdinalIgnoreCase))
        {
            score += 68;
        }
        else if (suggestion.Command.Contains("cargo watch", StringComparison.OrdinalIgnoreCase))
        {
            score += 66;
        }
        else if (ContainsAny(suggestion.Command, "dotnet run", "spring-boot:run", " bootRun", "go run", "cargo run", "uvicorn", "flask run", "manage.py runserver", "rails server", "bin/rails server", "mix phx.server"))
        {
            score += 48;
        }
        else if (label.Equals("Watch", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Serve", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Run", StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        if (TryGetDockerServiceName(suggestion, out var serviceName)
            && DockerComposeDiscovery.ClassifyService(serviceName) == DockerServiceRole.Api)
        {
            score += 62;
        }

        if (context.Classification.Has(ProjectStack.Node)
            && !IsLikelyFrontendNodeCommand(suggestion.Command, context.Directory)
            && ContainsAny(suggestion.Command, " run dev", " run start", "npm start"))
        {
            score += 28;
        }

        if (suggestion.Command.Contains("dotnet run", StringComparison.OrdinalIgnoreCase)
            && context.Suggestions.Any(item => item.Command.Contains("dotnet watch", StringComparison.OrdinalIgnoreCase)))
        {
            score -= 25;
        }

        if (suggestion.Command.Equals("cargo run", StringComparison.OrdinalIgnoreCase)
            && context.Suggestions.Any(item => item.Command.Contains("cargo watch", StringComparison.OrdinalIgnoreCase)))
        {
            score -= 25;
        }

        return score;
    }

    private static int ScoreFrontend(WorkspaceSetupTask suggestion, SuggestionContext context)
    {
        var label = NormalizeSuggestionLabel(suggestion.Label);
        var score = 0;
        if (label.Equals("Dev", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Start", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Serve", StringComparison.OrdinalIgnoreCase))
        {
            score += 45;
        }

        if (ContainsAny(suggestion.Command, " run dev", " run start", "deno task dev", "deno task start", "npm start", "vite", "next dev", "nuxt dev", "astro dev", "storybook"))
        {
            score += 42;
        }

        if (IsLikelyFrontendNodeCommand(suggestion.Command, context.Directory))
        {
            score += 18;
        }

        if (TryGetDockerServiceName(suggestion, out var serviceName)
            && DockerComposeDiscovery.ClassifyService(serviceName) == DockerServiceRole.Frontend)
        {
            score += 62;
        }

        return score;
    }

    private static int ScoreLogs(WorkspaceSetupTask suggestion, SuggestionContext context)
    {
        var label = NormalizeSuggestionLabel(suggestion.Label);
        var score = 0;
        if (label.Contains("log", StringComparison.OrdinalIgnoreCase)
            || suggestion.Command.Contains("logs", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (suggestion.Command.Contains(" tail", StringComparison.OrdinalIgnoreCase)
            || suggestion.Command.StartsWith("tail ", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (TryGetDockerServiceName(suggestion, out var serviceName))
        {
            score += DockerComposeDiscovery.ClassifyService(serviceName) switch
            {
                DockerServiceRole.Api => 48,
                DockerServiceRole.Frontend => 46,
                DockerServiceRole.Services => 44,
                _ => 42,
            };
        }
        else if (context.Suggestions.Any(item => TryGetDockerServiceName(item, out _))
                 && suggestion.Command.Equals("docker compose logs -f", StringComparison.OrdinalIgnoreCase))
        {
            score -= 20;
        }

        return score;
    }

    private static bool IsBlockedApiSuggestion(WorkspaceSetupTask suggestion, SuggestionContext context)
    {
        if (suggestion.Command.Contains("foreman start", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!context.Classification.Has(ProjectStack.Node))
        {
            return false;
        }

        if (IsLikelyFrontendNodeCommand(suggestion.Command, context.Directory))
        {
            return true;
        }

        var label = NormalizeSuggestionLabel(suggestion.Label);
        return label.Equals("Run", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                ProjectAnalysisAccessor.Instance.TryInferTaskType(context.Directory),
                TaskTypeCatalog.Frontend,
                StringComparison.Ordinal);
    }

    private static string NormalizeSuggestionLabel(string label)
    {
        const string prefix = "VS Code: ";
        return label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? label[prefix.Length..].Trim()
            : label;
    }

    private static int MonorepoFilterBonus(string taskType, string scriptValue)
    {
        if (string.IsNullOrWhiteSpace(scriptValue))
        {
            return 0;
        }

        var normalized = scriptValue.ToLowerInvariant();
        if (!normalized.Contains("turbo", StringComparison.Ordinal)
            && !normalized.Contains("nx ", StringComparison.Ordinal)
            && !normalized.Contains("nx run", StringComparison.Ordinal))
        {
            return 0;
        }

        return taskType switch
        {
            TaskTypeCatalog.Api when ContainsAny(normalized, "--filter=api", "--filter=backend", "--filter=server", "serve api", "dev:api") => 18,
            TaskTypeCatalog.Frontend when ContainsAny(normalized, "--filter=web", "--filter=frontend", "--filter=client", "--filter=app", "serve web", "dev:web") => 18,
            TaskTypeCatalog.Test when ContainsAny(normalized, "--filter=test", " run test") => 12,
            TaskTypeCatalog.Build when ContainsAny(normalized, "--filter=build", " run build") => 12,
            _ => 0,
        };
    }

    private static bool IsLikelyFrontendNodeCommand(string command, string directory) =>
        string.Equals(
            ProjectAnalysisAccessor.Instance.TryInferTaskType(directory),
            TaskTypeCatalog.Frontend,
            StringComparison.Ordinal)
        && ContainsAny(command, "npm ", "pnpm ", "yarn ", "bun ", "deno task");

    private static bool TryGetDockerServiceName(WorkspaceSetupTask suggestion, out string serviceName)
    {
        serviceName = string.Empty;
        const string upPrefix = "docker compose up ";
        const string logsPrefix = "docker compose logs -f ";

        if (suggestion.Command.StartsWith(upPrefix, StringComparison.OrdinalIgnoreCase))
        {
            serviceName = suggestion.Command[upPrefix.Length..].Trim();
            return serviceName.Length > 0 && !serviceName.Contains(' ', StringComparison.Ordinal);
        }

        if (suggestion.Command.StartsWith(logsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            serviceName = suggestion.Command[logsPrefix.Length..].Trim();
            return serviceName.Length > 0 && !serviceName.Contains(' ', StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsTestScriptName(string scriptName) =>
        scriptName is "test" or "tests" or "vitest" or "jest"
        || scriptName.StartsWith("test:", StringComparison.Ordinal)
        || scriptName.EndsWith(":test", StringComparison.Ordinal);

    private static bool IsBuildScriptName(string scriptName) =>
        scriptName is "build"
        || scriptName.StartsWith("build:", StringComparison.Ordinal)
        || scriptName.EndsWith(":build", StringComparison.Ordinal);

    private static bool IsApiScriptName(string scriptName, string scriptValue) =>
        scriptName is "api" or "server" or "backend"
        || scriptName.StartsWith("dev:api", StringComparison.Ordinal)
        || scriptName.Contains("api", StringComparison.Ordinal)
        || scriptName.Contains("server", StringComparison.Ordinal)
        || scriptName.Contains("backend", StringComparison.Ordinal)
        || ContainsAny(scriptValue, "--filter=api", "--filter=backend", "--filter=server");

    private static bool IsFrontendScriptName(string scriptName, string scriptValue) =>
        scriptName is "web" or "client" or "frontend" or "ui" or "storybook"
        || scriptName.StartsWith("dev:web", StringComparison.Ordinal)
        || scriptName.Contains("web", StringComparison.Ordinal)
        || scriptName.Contains("client", StringComparison.Ordinal)
        || scriptName.Contains("frontend", StringComparison.Ordinal)
        || scriptName.Contains("storybook", StringComparison.Ordinal)
        || scriptName.Contains("ui", StringComparison.Ordinal)
        || ContainsAny(scriptValue, "--filter=web", "--filter=frontend", "--filter=client", "--filter=app");

    private static bool IsLogsScriptName(string scriptName, string scriptValue) =>
        scriptName is "logs" or "log"
        || scriptName.Contains("logs", StringComparison.Ordinal)
        || scriptValue.Contains("logs", StringComparison.Ordinal);

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal sealed record SuggestionContext(
        string Directory,
        IReadOnlyList<WorkspaceSetupTask> Suggestions,
        ProjectClassification Classification);
}
