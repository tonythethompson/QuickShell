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
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public bool PortInUse(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch (SocketException)
            {
                // Best effort cleanup.
            }
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
        catch (Win32Exception)
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

            // Read both streams asynchronously so a full stderr buffer cannot deadlock
            // ReadToEnd, and so WaitForExit(timeout) can still kill a hung wsl.exe.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(3000))
            {
                TryKill(process);
                _ = Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(250));
                return [];
            }

            // Process exited; finish draining pipes without blocking forever.
            if (!Task.WaitAll([stdoutTask, stderrTask], 1000))
            {
                return [];
            }

            if (process.ExitCode != 0)
            {
                return [];
            }

            return stdoutTask.Result
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
        catch (AggregateException)
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
        catch (Win32Exception)
        {
            // Best effort.
        }
        catch (NotSupportedException)
        {
            // Best effort.
        }
    }
}
