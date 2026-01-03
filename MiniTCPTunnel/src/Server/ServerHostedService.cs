using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace MiniTCPTunnel.Server;

// 호스트 수명주기에 맞춰 ServerApp을 실행한다.
public sealed class ServerHostedService : BackgroundService
{
    private readonly ServerApp _app;

    public ServerHostedService(ServerApp app)
    {
        _app = app;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _app.RunAsync(stoppingToken);
    }
}
