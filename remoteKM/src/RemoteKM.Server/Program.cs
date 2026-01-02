using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace RemoteKM.Server;

internal static class Program
{
    internal static bool VerboseFlag = false;
    private static StreamWriter? _logWriter;

    [STAThread]
    private static void Main(string[] args)
    {
        InitializeLogging();
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
        LogStartupInfo(args);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext(settings));
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

            var fileName = $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log";
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

    private static void LogStartupInfo(string[] args)
    {
        try
        {
            Console.WriteLine($"Args: {string.Join(' ', args)}");
            Console.WriteLine($"ExePath: {Application.ExecutablePath}");
            Console.WriteLine($"BaseDir: {AppContext.BaseDirectory}");
            Console.WriteLine($"CurrentDir: {Environment.CurrentDirectory}");
            Console.WriteLine($"UserInteractive: {Environment.UserInteractive}");
            Console.WriteLine($"User: {WindowsIdentity.GetCurrent().Name}");
            Console.WriteLine($"IsAdmin: {IsAdministrator()}");
            Console.WriteLine($"SessionId: {Process.GetCurrentProcess().SessionId}");
            Console.WriteLine($"ProcessId: {Environment.ProcessId}");
            Console.WriteLine($"VerboseFlag: {VerboseFlag}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LogStartupInfo failed: {ex.Message}");
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
