using Microsoft.Extensions.DependencyInjection;
using QuickShell.Services;

namespace QuickShell.Composition;

/// <summary>
/// Composition root for <c>QuickShell.Core</c> services.
/// Prefer explicit factory registrations for AOT/trim analyzer friendliness.
/// </summary>
internal static class QuickShellServiceCollectionExtensions
{
    /// <summary>
    /// Registers core QuickShell services that the CmdPal host (and tests) resolve via DI.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configDirectory">
    /// Optional override for the shortcuts store directory (tests).
    /// When null, uses the default <c>%LOCALAPPDATA%\QuickShell</c> path.
    /// </param>
    public static IServiceCollection AddQuickShellCore(
        this IServiceCollection services,
        string? configDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAtomicFileWriter>(_ => new AtomicFileWriter());
        services.AddSingleton<IShortcutRepository>(sp =>
            new ShortcutRepository(configDirectory, sp.GetRequiredService<IAtomicFileWriter>()));
        services.AddSingleton<IDraftStore>(sp =>
            new ShortcutDraftStore(
                sp.GetRequiredService<IShortcutRepository>(),
                sp.GetRequiredService<IAtomicFileWriter>()));
        services.AddSingleton<ICommandIdParser>(_ => new CommandIdParser());

        return services;
    }
}
