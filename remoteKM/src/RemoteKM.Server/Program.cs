using System.Windows.Forms;

namespace RemoteKM.Server;

internal static class Program
{
    internal static bool VerboseFlag = false;

    [STAThread]
    private static void Main(string[] args)
    {
        var runProxy = false;
        var runAgent = false;

        foreach (var arg in args)
        {
            if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
            {
                VerboseFlag = true;
                continue;
            }

            if (arg.Equals("--proxy", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--headless", StringComparison.OrdinalIgnoreCase))
            {
                runProxy = true;
                continue;
            }

            if (arg.Equals("--agent", StringComparison.OrdinalIgnoreCase))
            {
                runAgent = true;
                continue;
            }
        }

        var settings = ServerSettings.FromArgs(args, ServerSettings.Load());

        if (runProxy)
        {
            ServerProxyHost.Run(settings);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext(settings, new ServerRuntimeOptions(runAgent)));
    }
}
