using System.IO;
using System.Windows.Forms;

namespace RemoteKM.Client;

internal static class Program
{
    internal static bool VerboseFlag = false;
    internal static bool EmergencyStopEnabled = false;
    private static StreamWriter? _logWriter;

    [STAThread]
    private static void Main(string[] args)
    {
        InitializeLogging();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ClientAppContext());
    }

    private static void InitializeLogging()
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RemoteKM",
                "logs");
            Directory.CreateDirectory(logDirectory);

            var fileName = $"client-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            var logPath = Path.Combine(logDirectory, fileName);
            _logWriter = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };

            Console.SetOut(_logWriter);
            Console.SetError(_logWriter);
            Console.WriteLine($"Log started: {DateTime.Now:O}");
            Console.WriteLine($"Log path: {logPath}");

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                _logWriter?.Dispose();
                _logWriter = null;
            };
        }
        catch
        {
        }
    }
}
