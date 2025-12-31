using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteKM.Client;

internal sealed class ClientProxyHost
{
    internal static void Run(string settingsPath, ClientSettings settings)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        RunAsync(settingsPath, settings, cts.Token).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(string settingsPath, ClientSettings initialSettings, CancellationToken token)
    {
        var current = initialSettings;
        while (!token.IsCancellationRequested)
        {
            var proxyPort = ProxyPortHelper.GetProxyPort(current.Port);
            var proxy = new TcpProxyService(IPAddress.Loopback, proxyPort, current.Host, current.Port);
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

            current = ClientSettings.Load(settingsPath);
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
