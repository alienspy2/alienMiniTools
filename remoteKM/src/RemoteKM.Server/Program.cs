using System.Windows.Forms;

namespace RemoteKM.Server;

internal static class Program
{
    internal static bool VerboseFlag = true;

    [STAThread]
    private static void Main(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-v", StringComparison.OrdinalIgnoreCase))
            {
                VerboseFlag = true;
                break;
            }
        }

        var settings = ServerSettings.FromArgs(args);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext(settings));
    }
}
