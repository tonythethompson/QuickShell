using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace QuickShell.Run;

/// <summary>
/// Registers plugin-directory assembly probing before PowerToys reflects on <see cref="Main"/>.
/// <see cref="Main"/>'s static constructor is too late: GetTypes resolves QuickShell.Core first.
/// </summary>
internal static class PluginDependencyResolver
{
    private static int _registered;

#pragma warning disable CA2255 // Intentional: PowerToys reflects on Main before its static ctor runs
    [ModuleInitializer]
    internal static void Initialize() => EnsureRegistered();
#pragma warning restore CA2255

    internal static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        var pluginDir = Path.GetDirectoryName(typeof(PluginDependencyResolver).Assembly.Location);
        if (string.IsNullOrEmpty(pluginDir))
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            if (string.IsNullOrEmpty(assemblyName.Name))
            {
                return null;
            }

            var candidate = Path.Combine(pluginDir, $"{assemblyName.Name}.dll");
            return File.Exists(candidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        };
    }
}
