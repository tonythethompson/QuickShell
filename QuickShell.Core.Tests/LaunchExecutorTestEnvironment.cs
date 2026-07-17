namespace QuickShell.Core.Tests;

/// <summary>
/// Compatibility shim: terminal discovery stubs only. Launch/health/git are constructed
/// per test via <see cref="LaunchTestServices.CreateBundle"/>.
/// </summary>
internal static class LaunchExecutorTestEnvironment
{
    public static void Apply() => LaunchTestServices.ApplyTerminalDiscoveryStubs();

    public static void Reset() => LaunchTestServices.ResetTerminalDiscoveryStubs();
}
