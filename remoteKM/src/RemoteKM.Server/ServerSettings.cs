using System.IO;
using System.Net;
using System.Text.Json;

namespace RemoteKM.Server;

internal sealed class ServerSettings
{
    private const string SettingsFileName = "server.settings.json";

    internal string Host { get; }
    internal int Port { get; }

    internal IPAddress IpAddress => IPAddress.Parse(Host);

    internal ServerSettings(string host, int port)
    {
        Host = host;
        Port = port;
    }

    internal static ServerSettings FromArgs(string[] args, ServerSettings defaults)
    {
        var host = defaults.Host;
        var port = defaults.Port;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--host", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                host = args[++i];
            }
            else if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsed))
                {
                    port = parsed;
                }
            }
        }

        return new ServerSettings(host, port);
    }

    internal static ServerSettings Load()
    {
        var defaults = new ServerSettings("0.0.0.0", 5000);
        var path = SettingsPath;
        try
        {
            if (!File.Exists(path))
            {
                return defaults;
            }

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ServerSettingsData>(json);
            if (data == null || string.IsNullOrWhiteSpace(data.Host))
            {
                return defaults;
            }

            var port = data.Port is >= 1 and <= 65535 ? data.Port : defaults.Port;
            return new ServerSettings(data.Host, port);
        }
        catch
        {
            return defaults;
        }
    }

    internal static void Save(ServerSettings settings)
    {
        var path = SettingsPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var data = new ServerSettingsData(settings.Host, settings.Port);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    internal static string SettingsPath
    {
        get
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(basePath, "RemoteKM", SettingsFileName);
        }
    }

    private sealed record ServerSettingsData(string Host, int Port);
}
