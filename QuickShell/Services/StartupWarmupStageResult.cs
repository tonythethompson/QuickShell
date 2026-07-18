using System;

namespace QuickShell.Services;

/// <summary>
/// Outcome of one startup warmup stage recorded by <see cref="StartupWarmupCoordinator"/>.
/// </summary>
internal sealed record StartupWarmupStageResult(
    string Name,
    TimeSpan Duration,
    string? Outcome);
