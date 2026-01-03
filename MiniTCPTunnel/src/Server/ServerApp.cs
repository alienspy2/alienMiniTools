using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniTCPTunnel.Shared.Config;

namespace MiniTCPTunnel.Server;

// 서버의 제어 채널 수락 루프를 담당한다.
public sealed class ServerApp
{
    private readonly ILogger<ServerApp> _logger;
    private readonly ServerConfig _config;
    private int _activeClient;

    public ServerApp(ILogger<ServerApp> logger, IOptions<ServerConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var address = ResolveListenAddress(_config.ControlListenHost);
        var listener = new TcpListener(address, _config.ControlPort);

        listener.Start();
        _logger.LogInformation("제어 채널 리스너 시작: {Address}:{Port}", address, _config.ControlPort);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 정상 종료 경로이므로 로그를 줄인다.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            // 단일 클라이언트 정책을 보장하기 위해 경쟁을 차단한다.
            if (Interlocked.CompareExchange(ref _activeClient, 1, 0) != 0)
            {
                _logger.LogWarning("이미 활성 클라이언트가 있어 연결을 거절합니다.");
                return;
            }

            try
            {
                _logger.LogInformation("클라이언트 접속: {Remote}", client.Client.RemoteEndPoint);
                await WaitForDisconnectAsync(client, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _activeClient, 0);
            }
        }
    }

    private static async Task WaitForDisconnectAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // 현재는 핸드셰이크 이전 단계이므로 단순히 연결 종료를 감지한다.
        var buffer = new byte[1];
        var stream = client.GetStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
        }
    }

    private static IPAddress ResolveListenAddress(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" || host == "*")
        {
            return IPAddress.Any;
        }

        if (host == "::")
        {
            return IPAddress.IPv6Any;
        }

        return IPAddress.Parse(host);
    }
}
