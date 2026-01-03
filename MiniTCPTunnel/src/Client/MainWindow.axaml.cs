using System;
using Avalonia.Controls;
using MiniTCPTunnel.Shared.Config;

namespace MiniTCPTunnel.Client;

// XAML로 정의된 메인 윈도우의 코드 비하인드.
public sealed partial class MainWindow : Window
{
    // ViewModel 이벤트 구독을 해제하기 위해 참조를 보관한다.
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        // XAML 컴포넌트를 로드해 UI 요소를 연결한다.
        InitializeComponent();

        // DataContext가 교체될 수 있으므로 변경 시점에 이벤트를 다시 연결한다.
        DataContextChanged += OnDataContextChanged;

        // 닫기 버튼을 누르면 종료 대신 트레이로 숨기도록 가로챈다.
        Closing += OnClosing;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 이전 ViewModel에 연결된 이벤트를 해제해 중복 호출을 방지한다.
        if (_viewModel is not null)
        {
            _viewModel.SettingsRequested -= OnSettingsRequested;
        }

        // 새 ViewModel을 연결하고 설정 창 요청 이벤트를 구독한다.
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.SettingsRequested += OnSettingsRequested;
        }
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        // 실행 인자로 전달된 --config 옵션을 우선 반영해 설정 파일 경로를 계산한다.
        var configPath = ConfigPathResolver.ResolveConfigPath(App.StartupArgs, "client.json");

        // 설정 ViewModel을 생성하고 현재 설정 값을 채운다.
        var settingsViewModel = ServerSettingsViewModel.Load(configPath);

        // 설정 창을 모달로 띄워 사용자 입력이 끝날 때까지 대기한다.
        var settingsWindow = new ServerSettingsWindow
        {
            DataContext = settingsViewModel
        };

        await settingsWindow.ShowDialog(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // 트레이에서 종료를 선택한 경우에는 정상 종료를 허용한다.
        if (App.ExitRequested)
        {
            return;
        }

        // 일반 닫기 동작은 취소하고 창을 숨겨 트레이로 최소화한다.
        e.Cancel = true;
        Hide();
    }
}
