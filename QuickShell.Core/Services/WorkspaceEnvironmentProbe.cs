using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace QuickShell.Services;

using QuickShell.Abstractions;

/// <summary>
/// Production host/environment probes for workspace health checks.
/// </summary>
internal sealed class WorkspaceEnvironmentProbe : IWorkspaceEnvironmentProbe
{
    public bool ExecutableExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (File.Exists(path))
        {
            return true;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(1500))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool PortInUse(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    public IReadOnlyList<string> ProcessNames()
    {
        try
        {
            return Process.GetProcesses()
                .Select(process => process.ProcessName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
        catch (SystemException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> WslDistroNames()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-l -q",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                TryKill(process);
                return [];
            }

            if (process.ExitCode != 0)
            {
                return [];
            }

            return output
                .Replace("\0", string.Empty, StringComparison.Ordinal)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Win32Exception)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Best effort.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Best effort.
        }
        catch (NotSupportedException)
        {
            // Best effort.
        }
    }
}
