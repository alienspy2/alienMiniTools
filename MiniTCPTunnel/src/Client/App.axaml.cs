using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace MiniTCPTunnel.Client;

// Avalonia 애플리케이션 루트: 앱 시작 시 메인 창을 구성한다.
public sealed partial class App : Application
{
    // 프로그램 시작 인자(예: --config)를 보관해 설정 창에서도 동일한 경로를 쓰도록 한다.
    public static string[] StartupArgs { get; set; } = Array.Empty<string>();

    // DI 컨테이너를 보관해 ViewModel을 생성할 때 사용한다.
    public static IServiceProvider? Services { get; set; }

    // 사용자가 트레이에서 "종료"를 선택했는지 여부를 표시한다.
    public static bool ExitRequested { get; private set; }

    public override void Initialize()
    {
        // XAML 리소스를 로드해 스타일/리소스가 적용되도록 한다.
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 데스크톱 런타임에서만 메인 창을 구성한다.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // DI 컨테이너가 있으면 연결 제어가 가능한 ViewModel을 사용한다.
            var viewModel = Services?.GetService<MainWindowViewModel>() ?? new MainWindowViewModel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayOpenClick(object? sender, EventArgs e)
    {
        // 트레이 메뉴에서 창 열기를 선택하면 숨겨진 메인 창을 다시 보여준다.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } window)
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        // 트레이 메뉴에서 종료를 선택하면 종료 플래그를 세우고 앱을 종료한다.
        ExitRequested = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } window)
        {
            window.Close();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown();
        }
    }
}
