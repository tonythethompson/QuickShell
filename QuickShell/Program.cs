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
        // #region agent log
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                AgentDebugLog.WriteException("Program.cs:UnhandledException", ex, hypothesisId: "E");
            }
            else
            {
                AgentDebugLog.Write(
                    "Program.cs:UnhandledException",
                    "non-exception unhandled",
                    new { value = eventArgs.ExceptionObject?.ToString() },
                    hypothesisId: "E");
            }
        };
        AgentDebugLog.Write(
            "Program.cs:Main",
            "entry",
            new { argCount = args.Length, firstArg = args.Length > 0 ? args[0] : null },
            hypothesisId: "E");
        // #endregion

        if (args.Length == 0 || string.Equals(args[0], "-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase))
        {
            RunComServer();
            return;
        }

        Console.WriteLine("Not being launched as a Extension... exiting.");
    }

    private static void RunComServer()
    {
        // #region agent log
        AgentDebugLog.Write("Program.cs:RunComServer", "start", hypothesisId: "E");
        // #endregion

        global::Shmuelie.WinRTServer.ComServer server = new();

        ManualResetEvent extensionDisposedEvent = new(false);
        QuickShellExtension extensionInstance;
        try
        {
            // #region agent log
            AgentDebugLog.Write("Program.cs:RunComServer", "creating extension", hypothesisId: "E");
            // #endregion
            extensionInstance = new QuickShellExtension(extensionDisposedEvent);
            // #region agent log
            AgentDebugLog.Write("Program.cs:RunComServer", "extension created", hypothesisId: "E");
            // #endregion
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugLog.WriteException("Program.cs:RunComServer", ex, hypothesisId: "E");
            // #endregion
            throw;
        }

        server.RegisterClass<QuickShellExtension, IExtension>(() => extensionInstance);
        server.Start();

        // #region agent log
        AgentDebugLog.Write("Program.cs:RunComServer", "com server started", hypothesisId: "E");
        // #endregion

        extensionDisposedEvent.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
