using CommunityToolkit.Mvvm.ComponentModel;

namespace MiniTCPTunnel.Client;

// 터널 리스트 한 줄을 표현하는 ViewModel.
public sealed partial class TunnelRowViewModel : ObservableObject
{
    // 사용자에게 보여줄 터널 이름/식별자.
    [ObservableProperty]
    private string _name;

    // 로컬/원격 엔드포인트 요약 문자열.
    [ObservableProperty]
    private string _endpointSummary;

    // 활성/비활성 등의 상태 문자열.
    [ObservableProperty]
    private string _statusText;

    // 현재 연결 수.
    [ObservableProperty]
    private int _connectionCount;

    // 연결 수를 사람이 읽기 쉬운 텍스트로 제공한다.
    public string ConnectionCountText => $"연결 {ConnectionCount}건";

    public TunnelRowViewModel(string name, string endpointSummary, string statusText, int connectionCount)
    {
        _name = name;
        _endpointSummary = endpointSummary;
        _statusText = statusText;
        _connectionCount = connectionCount;
    }

    // 연결 수가 바뀌면 텍스트도 갱신되도록 알림을 추가한다.
    partial void OnConnectionCountChanged(int value)
    {
        OnPropertyChanged(nameof(ConnectionCountText));
    }
}
