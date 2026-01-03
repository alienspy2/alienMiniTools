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
    // 접속/해제 상태와 현재 접속 시도 토큰을 보호하기 위한 락 객체.
    private readonly object _stateLock = new();
    // "접속 요청됨" 신호를 비동기 대기하기 위한 TaskCompletionSource.
    private TaskCompletionSource<bool> _connectRequestedSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    // 현재 접속 시도(또는 연결 유지 루프)에 사용 중인 CancellationTokenSource.
    private CancellationTokenSource? _connectAttemptCts;
    // 접속 시도 요청 여부(버튼으로 토글됨).
    private bool _connectRequested;
    // 실제 연결이 성공적으로 성립되었는지 여부.
    private bool _isConnected;

    // 연결 상태 변화(연결됨/끊김)를 UI로 알리기 위한 이벤트.
    public event EventHandler<bool>? ConnectionStateChanged;

    public ClientApp(ILogger<ClientApp> logger, IOptions<ClientConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    // "접속" 버튼을 눌렀을 때 호출: 접속 시도를 허용하고 대기 루프를 깨운다.
    public void RequestConnect()
    {
        lock (_stateLock)
        {
            if (_connectRequested)
            {
                return;
            }

            _connectRequested = true;
            _connectRequestedSignal.TrySetResult(true);
        }
    }

    // "해제" 버튼을 눌렀을 때 호출: 접속 시도를 중단하고 현재 연결을 끊는다.
    public void RequestDisconnect()
    {
        lock (_stateLock)
        {
            if (!_connectRequested)
            {
                return;
            }

            _connectRequested = false;
            _connectAttemptCts?.Cancel();
            // 다음 접속 요청을 위해 대기 신호를 새로 만든다.
            _connectRequestedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // UI에 즉시 끊김 상태를 알린다.
        UpdateConnectionState(false);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 접속 버튼이 눌릴 때까지 대기한다.
                await WaitForConnectRequestedAsync(cancellationToken).ConfigureAwait(false);

                // 접속 요청이 유지되는 동안만 재접속 루프를 수행한다.
                while (!cancellationToken.IsCancellationRequested && IsConnectRequested())
                {
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    SetConnectAttemptCts(connectCts);

                    try
                    {
                        await ConnectAndRunAsync(connectCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (connectCts.IsCancellationRequested)
                    {
                        // 해제 요청 또는 종료 요청에 의한 취소는 정상 흐름이다.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "서버 연결에 실패했습니다. {Delay} 후 재시도합니다.", retryDelay);
                    }
                    finally
                    {
                        UpdateConnectionState(false);
                        SetConnectAttemptCts(null);
                    }

                    if (!IsConnectRequested() || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // 재시도 대기 중에도 해제 요청이 들어오면 즉시 빠져나간다.
                    try
                    {
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        _logger.LogInformation("서버 연결 시도: {Host}:{Port}", _config.Server.Host, _config.Server.ControlPort);

        await client.ConnectAsync(_config.Server.Host, _config.Server.ControlPort, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("서버 연결 성공");

        UpdateConnectionState(true);

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

    // 접속 요청 여부를 안전하게 읽는다.
    private bool IsConnectRequested()
    {
        lock (_stateLock)
        {
            return _connectRequested;
        }
    }

    // 접속 요청 신호를 기다린다(요청이 이미 있으면 즉시 반환).
    private Task WaitForConnectRequestedAsync(CancellationToken cancellationToken)
    {
        Task waitTask;

        lock (_stateLock)
        {
            if (_connectRequested)
            {
                return Task.CompletedTask;
            }

            waitTask = _connectRequestedSignal.Task;
        }

        return waitTask.WaitAsync(cancellationToken);
    }

    // 현재 접속 시도에 사용 중인 CancellationTokenSource를 갱신한다.
    private void SetConnectAttemptCts(CancellationTokenSource? cts)
    {
        lock (_stateLock)
        {
            _connectAttemptCts = cts;
        }
    }

    // 연결 상태를 갱신하고, 변화가 있을 때만 이벤트로 알린다.
    private void UpdateConnectionState(bool isConnected)
    {
        var shouldNotify = false;

        lock (_stateLock)
        {
            if (_isConnected == isConnected)
            {
                return;
            }

            _isConnected = isConnected;
            shouldNotify = true;
        }

        if (shouldNotify)
        {
            ConnectionStateChanged?.Invoke(this, isConnected);
        }
    }
}
