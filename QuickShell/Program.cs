using Microsoft.CommandPalette.Extensions;
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
        if (args.Length == 0 || string.Equals(args[0], "-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase))
        {
            RunComServer();
            return;
        }

        Console.WriteLine("Not being launched as a Extension... exiting.");
    }

    private static void RunComServer()
    {
        global::Shmuelie.WinRTServer.ComServer server = new();

        ManualResetEvent extensionDisposedEvent = new(false);
        QuickShellExtension extensionInstance = new(extensionDisposedEvent);
        server.RegisterClass<QuickShellExtension, IExtension>(() => extensionInstance);
        server.Start();

        extensionDisposedEvent.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
