using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace MiniTCPTunnel.Client;

// 호스트 수명주기에 맞춰 ClientApp을 실행한다.
public sealed class ClientHostedService : BackgroundService
{
    private readonly ClientApp _app;

    public ClientHostedService(ClientApp app)
    {
        _app = app;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _app.RunAsync(stoppingToken);
    }
}
