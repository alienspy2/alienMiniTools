using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniTCPTunnel.Shared.Config;

namespace MiniTCPTunnel.Client;

// 클라이언트 제어 채널 연결 및 재접속 루프를 담당한다.
public sealed class ClientApp
{
    private readonly ILogger<ClientApp> _logger;
    private readonly ClientConfig _config;

    public ClientApp(ILogger<ClientApp> logger, IOptions<ClientConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "서버 연결에 실패했습니다. {Delay} 후 재시도합니다.", retryDelay);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        _logger.LogInformation("서버 연결 시도: {Host}:{Port}", _config.Server.Host, _config.Server.ControlPort);

        await client.ConnectAsync(_config.Server.Host, _config.Server.ControlPort, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("서버 연결 성공");

        using var stream = client.GetStream();

        // TODO: 핸드셰이크/인증 후 제어 프레임 루프를 시작한다.
        await RunHeartbeatLoopAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunHeartbeatLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(5);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Heartbeat 전송 예정 (구현 전)");
        }
    }
}
