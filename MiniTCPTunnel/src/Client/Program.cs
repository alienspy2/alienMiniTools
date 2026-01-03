using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;

namespace MiniTCPTunnel.Client;

// GUI 진입점: Avalonia 앱을 실행하면서 백그라운드 클라이언트 로직을 함께 시작한다.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var guiHost = ClientGuiHost.Create(args);

        try
        {
            // UI가 뜨기 전에 네트워크/설정 백그라운드 작업을 시작한다.
            await guiHost.StartAsync().ConfigureAwait(false);

            // 설정 창에서도 동일한 실행 인자를 참조할 수 있도록 전달한다.
            App.StartupArgs = args;
            // ViewModel이 DI를 통해 ClientApp에 접근할 수 있도록 서비스 컨테이너를 전달한다.
            App.Services = guiHost.Services;

            // GUI 런타임을 실행하고 종료 코드를 반환한다.
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // 앱 종료 시 백그라운드 호스트를 안전하게 정리한다.
            await guiHost.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // 플랫폼 자동 감지 + ReactiveUI 연동 설정.
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .LogToTrace();
    }
}
