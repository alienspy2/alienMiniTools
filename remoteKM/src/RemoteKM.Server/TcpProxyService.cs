using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteKM.Server;

internal sealed class TcpProxyService
{
    private readonly IPAddress _listenIp;
    private readonly int _listenPort;
    private readonly IPAddress _targetIp;
    private readonly int _targetPort;

    internal TcpProxyService(IPAddress listenIp, int listenPort, IPAddress targetIp, int targetPort)
    {
        _listenIp = listenIp;
        _listenPort = listenPort;
        _targetIp = targetIp;
        _targetPort = targetPort;
    }

    internal Task RunAsync(CancellationToken token)
    {
        var mainTask = RunListenerAsync(_listenPort, _targetPort, token);
        var transferTask = RunListenerAsync(_listenPort + 1, _targetPort + 1, token);
        return Task.WhenAll(mainTask, transferTask);
    }

    private async Task RunListenerAsync(int listenPort, int targetPort, CancellationToken token)
    {
        var listener = new TcpListener(_listenIp, listenPort);
        listener.Start();
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(() => ProxyConnectionAsync(client, targetPort, token));
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ProxyConnectionAsync(TcpClient inbound, int targetPort, CancellationToken token)
    {
        using (inbound)
        {
            inbound.NoDelay = true;
            using var outbound = new TcpClient();
            if (!await TryConnectAsync(outbound, _targetIp, targetPort, token))
            {
                return;
            }

            outbound.NoDelay = true;

            using var inboundStream = inbound.GetStream();
            using var outboundStream = outbound.GetStream();

            var upstream = PumpAsync(inboundStream, outboundStream, token);
            var downstream = PumpAsync(outboundStream, inboundStream, token);
            await Task.WhenAny(upstream, downstream);
        }
    }

    private static async Task<bool> TryConnectAsync(TcpClient client, IPAddress targetIp, int targetPort, CancellationToken token)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts && !token.IsCancellationRequested; attempt++)
        {
            try
            {
                await client.ConnectAsync(targetIp, targetPort, token);
                return true;
            }
            catch (SocketException)
            {
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await Task.Delay(500, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }

    private static async Task PumpAsync(NetworkStream source, NetworkStream destination, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            try
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }
}
