using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MiniTCPTunnel.Client;

// 메인 화면 상태를 표현하는 ViewModel: 터널 목록과 요약 상태를 바인딩한다.
public sealed partial class MainWindowViewModel : ObservableObject
{
    // 접속/해제 요청을 전달할 클라이언트 백그라운드 서비스.
    private readonly ClientApp? _clientApp;

    // 설정 창 열기 요청을 View로 전달하기 위한 이벤트.
    public event EventHandler? SettingsRequested;

    // 상단 상태 문구(서버 연결 여부 등)를 표시한다.
    [ObservableProperty]
    private string _statusText = "서버 연결 대기 중...";

    // 현재 활성 연결 수를 집계해 표시한다.
    [ObservableProperty]
    private int _activeConnections = 0;

    // 클라이언트 실행 상태(연결 시도/정지 등)를 표시한다.
    [ObservableProperty]
    private bool _isRunning = false;

    // 서버 연결 여부를 표시한다(연결됨/연결 중).
    [ObservableProperty]
    private bool _isConnected = false;

    // UI에 표시할 터널 목록.
    public ObservableCollection<TunnelRowViewModel> Tunnels { get; } = new();

    // 상단 요약 텍스트를 간단히 보여준다.
    public string ConnectionSummary => $"연결 수: {ActiveConnections}";

    // 실행 여부를 사람이 읽기 쉬운 텍스트로 변환한다.
    public string RunningSummary => IsRunning ? "상태: 실행 중" : "상태: 중지";

    // 상단 알림 패널의 문구(연결됨/연결 중)를 제공한다.
    public string ConnectionBadgeText => IsConnected ? "연결됨" : "연결 중";

    public MainWindowViewModel(ClientApp? clientApp = null)
    {
        _clientApp = clientApp;
        if (_clientApp is not null)
        {
            // 백그라운드 연결 상태를 UI에 반영하기 위해 이벤트를 구독한다.
            _clientApp.ConnectionStateChanged += OnConnectionStateChanged;
        }

        // 초기 화면을 위한 샘플 데이터를 채워 UI 형태를 확인한다.
        // TODO: 실제 TunnelManager 연동 시 이 부분은 상태 스토어로 교체한다.
        Tunnels.Add(new TunnelRowViewModel(
            "web",
            "localhost:80 -> public:8080",
            "비활성",
            0));
        Tunnels.Add(new TunnelRowViewModel(
            "ssh",
            "localhost:22 -> public:10022",
            "활성",
            2));
    }

    // 상단 "접속" 버튼을 눌렀을 때의 동작(현재는 UI 상태만 갱신).
    [RelayCommand]
    private void Connect()
    {
        // 사용자가 접속 버튼을 누른 시점부터 접속 시도를 시작한다.
        IsRunning = true;
        IsConnected = false;
        StatusText = "서버 연결 시도 중...";

        _clientApp?.RequestConnect();
    }

    // 상단 "해제" 버튼을 눌렀을 때의 동작(현재는 UI 상태만 갱신).
    [RelayCommand]
    private void Disconnect()
    {
        // 해제 시점부터 접속 시도를 중단하고 현재 연결을 끊는다.
        IsRunning = false;
        IsConnected = false;
        StatusText = "서버 연결 시도가 중지되었습니다.";

        _clientApp?.RequestDisconnect();
    }

    // 상단 "설정" 버튼을 눌렀을 때 설정 창을 열도록 요청한다.
    [RelayCommand]
    private void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    // "추가" 버튼 클릭 시 동작하는 명령(현재는 자리 표시).
    [RelayCommand]
    private void AddTunnel()
    {
        StatusText = "터널 추가 다이얼로그는 추후 연결 예정입니다.";
    }

    // "수정" 버튼 클릭 시 동작하는 명령(현재는 자리 표시).
    [RelayCommand]
    private void EditTunnel()
    {
        StatusText = "터널 수정 다이얼로그는 추후 연결 예정입니다.";
    }

    // "삭제" 버튼 클릭 시 동작하는 명령(현재는 자리 표시).
    [RelayCommand]
    private void RemoveTunnel()
    {
        StatusText = "터널 삭제 다이얼로그는 추후 연결 예정입니다.";
    }

    // 연결 수가 바뀌면 요약 텍스트도 갱신되도록 알림을 추가한다.
    partial void OnActiveConnectionsChanged(int value)
    {
        OnPropertyChanged(nameof(ConnectionSummary));
    }

    // 연결 여부가 바뀌면 상단 알림 문구도 갱신되도록 알림을 추가한다.
    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionBadgeText));
    }

    // 실행 상태가 바뀌면 요약 텍스트도 갱신되도록 알림을 추가한다.
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunningSummary));
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        // 백그라운드 스레드에서 들어오는 이벤트이므로 UI 스레드로 전환한다.
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = isConnected;
            // 사용자가 해제 버튼을 눌러 중지한 경우에는 상태 문구를 덮어쓰지 않는다.
            if (!IsRunning && !isConnected)
            {
                return;
            }

            StatusText = isConnected
                ? "서버에 연결되었습니다."
                : "서버 연결이 끊겼습니다.";
        });
    }
}
