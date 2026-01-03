using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniTCPTunnel.Shared.Config;

namespace MiniTCPTunnel.Client;

// GUI 앱에서 백그라운드로 클라이언트 로직(제어 채널 등)을 돌리기 위한 호스트 래퍼.
public sealed class ClientGuiHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _started;
    private bool _disposed;

    private ClientGuiHost(IHost host)
    {
        _host = host;
    }

    // GUI에서 DI 컨테이너에 접근할 수 있도록 서비스 공급자를 노출한다.
    public IServiceProvider Services => _host.Services;

    public static ClientGuiHost Create(string[] args)
    {
        // 기존 콘솔 앱의 구성 방식을 그대로 재사용해 설정/DI를 유지한다.
        var builder = Host.CreateApplicationBuilder(args);

        var configPath = ConfigPathResolver.ResolveConfigPath(args, "client.json");

        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile(configPath, optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        builder.Services.Configure<ClientConfig>(builder.Configuration);
        builder.Services.AddSingleton<ClientApp>();
        builder.Services.AddHostedService<ClientHostedService>();
        // GUI ViewModel도 DI로 관리해 ClientApp과 연결되도록 한다.
        builder.Services.AddSingleton<MainWindowViewModel>();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        return new ClientGuiHost(builder.Build());
    }

    public async Task StartAsync()
    {
        // 중복 시작을 막아 GUI 초기화와 충돌하지 않도록 한다.
        if (_started)
        {
            return;
        }

        await _host.StartAsync().ConfigureAwait(false);
        _started = true;
    }

    public async ValueTask DisposeAsync()
    {
        // 종료가 중복 호출되지 않도록 보호한다.
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_started)
        {
            await _host.StopAsync().ConfigureAwait(false);
        }

        _host.Dispose();
    }
}
