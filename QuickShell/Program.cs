using Microsoft.CommandPalette.Extensions;
using QuickShell.Services;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Threading;

namespace QuickShell;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        RepositoryDiagnostics.Sink = (location, eventName, elapsedMs) =>
            SupportDiagnostics.Default.Write(location, eventName, elapsedMs is null ? null : new { elapsedMs });

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                SupportDiagnostics.Default.WriteException("Program.cs:UnhandledException", ex);
            }
            else
            {
                SupportDiagnostics.Default.Write(
                    "Program.cs:UnhandledException",
                    "non-exception unhandled",
                    new { value = eventArgs.ExceptionObject?.ToString() });
            }
        };
        SupportDiagnostics.Default.Write(
            "Program.cs:Main",
            "entry",
            new { argCount = args.Length, firstArg = args.Length > 0 ? args[0] : null });

        if (args.Length == 0 || string.Equals(args[0], "-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase))
        {
            RunComServer();
            return;
        }

        Console.WriteLine("Not being launched as a Extension... exiting.");
    }

    private static void RunComServer()
    {
        SupportDiagnostics.Default.Write("Program.cs:RunComServer", "start");

        global::Shmuelie.WinRTServer.ComServer server = new();

        ManualResetEvent extensionDisposedEvent = new(false);
        QuickShellExtension extensionInstance;
        try
        {
            SupportDiagnostics.Default.Write("Program.cs:RunComServer", "creating extension");
            extensionInstance = new QuickShellExtension(extensionDisposedEvent);
            SupportDiagnostics.Default.Write("Program.cs:RunComServer", "extension created");
        }
        catch (Exception ex)
        {
            SupportDiagnostics.Default.WriteException("Program.cs:RunComServer", ex);
            throw;
        }

        server.RegisterClass<QuickShellExtension, IExtension>(() => extensionInstance);
        server.Start();

        SupportDiagnostics.Default.Write("Program.cs:RunComServer", "com server started");

        try
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                SupportDiagnostics.Default.Write("Program.cs:CancelKeyPress", "signal-exit");
                extensionDisposedEvent.Set();
            };
        }
        catch (Exception ex)
        {
            SupportDiagnostics.Default.WriteException("Program.cs:CancelKeyPress", ex);
        }

        extensionDisposedEvent.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
