using System.Net;

namespace RemoteKM.Server;

internal sealed class ServerSettings
{
    internal string Host { get; }
    internal int Port { get; }

    internal IPAddress IpAddress => IPAddress.Parse(Host);

    internal ServerSettings(string host, int port)
    {
        Host = host;
        Port = port;
    }

    internal static ServerSettings FromArgs(string[] args)
    {
        var host = "0.0.0.0";
        var port = 5000;

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
}
