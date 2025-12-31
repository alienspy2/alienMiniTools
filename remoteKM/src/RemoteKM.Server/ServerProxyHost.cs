using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteKM.Server;

internal sealed class ServerProxyHost
{
    internal static void Run(ServerSettings settings)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        RunAsync(settings, cts.Token).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(ServerSettings initialSettings, CancellationToken token)
    {
        var settingsPath = ServerSettings.SettingsPath;
        var current = initialSettings;

        while (!token.IsCancellationRequested)
        {
            var proxyPort = ProxyPortHelper.GetProxyPort(current.Port);
            var proxy = new TcpProxyService(current.IpAddress, current.Port, IPAddress.Loopback, proxyPort);
            var reloadTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var watcher = CreateWatcher(settingsPath, () => reloadTcs.TrySetResult(true));
            using var proxyCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            var proxyTask = proxy.RunAsync(proxyCts.Token);
            var completed = await Task.WhenAny(proxyTask, reloadTcs.Task);

            if (completed != reloadTcs.Task)
            {
                break;
            }

            await Task.Delay(200, token);
            proxyCts.Cancel();
            try
            {
                await proxyTask;
            }
            catch
            {
            }

            current = ServerSettings.Load();
        }
    }

    private static FileSystemWatcher CreateWatcher(string settingsPath, Action onChange)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }
        Directory.CreateDirectory(directory);

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(settingsPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        FileSystemEventHandler handler = (_, _) => onChange();
        RenamedEventHandler renameHandler = (_, _) => onChange();
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Renamed += renameHandler;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}
