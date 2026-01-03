using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MiniTCPTunnel.Client;

// 서버 설정 다이얼로그의 코드 비하인드: 저장/취소 버튼을 처리한다.
public sealed partial class ServerSettingsWindow : Window
{
    public ServerSettingsWindow()
    {
        // XAML 컴포넌트를 로드해 UI 요소를 연결한다.
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        // 변경 사항을 저장하지 않고 창을 닫는다.
        Close(false);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // ViewModel에서 검증/저장 후 성공했을 때만 닫는다.
        if (DataContext is not ServerSettingsViewModel viewModel)
        {
            return;
        }

        var saved = viewModel.TrySave();
        if (saved)
        {
            Close(true);
        }
    }
}
