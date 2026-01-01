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
#if !DEBUG
        if (!EnsureElevated(args))
        {
            return;
        }
#endif
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

    private static bool EnsureElevated(string[] args)
    {
        if (IsAdministrator())
        {
            return true;
        }

        if (HasArgument(args, "--elevated"))
        {
            Console.WriteLine("Elevation requested but still not elevated.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = BuildArguments(args, "--elevated"),
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Console.WriteLine("Elevation canceled by user.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Elevation failed: {ex.Message}");
        }

        return false;
    }

    private static bool HasArgument(string[] args, string value)
    {
        foreach (var arg in args)
        {
            if (arg.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildArguments(string[] args, string extraArg)
    {
        var items = new string[args.Length + 1];
        var index = 0;
        foreach (var arg in args)
        {
            items[index++] = QuoteArgument(arg);
        }

        items[index] = QuoteArgument(extraArg);
        return string.Join(" ", items);
    }

    private static string QuoteArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return arg;
        }

        return $"\"{arg.Replace("\"", "\\\"")}\"";
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
