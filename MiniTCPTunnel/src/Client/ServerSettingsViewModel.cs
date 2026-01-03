using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniTCPTunnel.Shared.Config;

namespace MiniTCPTunnel.Client;

// 서버 설정 창에서 입력받는 값을 관리하고, client.json에 저장하는 ViewModel.
public sealed partial class ServerSettingsViewModel : ObservableObject
{
    // 설정 파일 경로는 생성 시 고정하며, 저장 시 동일한 경로에 다시 기록한다.
    private readonly string _configPath;

    // 원본 설정을 유지해 다른 항목(tunnels, identityKeyPath 등)을 보존한다.
    private ClientConfig _config;

    // 설정 파일 경로를 UI에서 표시할 수 있도록 노출한다.
    public string ConfigPath => _configPath;

    // 서버 호스트(도메인/IP) 입력값.
    [ObservableProperty]
    private string _host = "localhost";

    // 컨트롤 포트 입력값(문자열로 받아서 검증 후 int로 변환).
    [ObservableProperty]
    private string _controlPortText = "9000";

    // 서버 공개키 입력값(없을 수 있으므로 빈 문자열 허용).
    [ObservableProperty]
    private string _serverPublicKey = string.Empty;

    // 저장 실패/성공 메시지를 표시하기 위한 상태 텍스트.
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private ServerSettingsViewModel(string configPath, ClientConfig config)
    {
        _configPath = configPath;
        _config = config;
    }

    // 설정 파일을 읽어 현재 상태로 ViewModel을 초기화한다.
    public static ServerSettingsViewModel Load(string configPath)
    {
        var config = new ClientConfig();
        var statusMessage = string.Empty;

        try
        {
            if (File.Exists(configPath))
            {
                // JSON을 읽어 클라이언트 설정으로 역직렬화한다(대소문자 구분 없이 파싱).
                var json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                config = JsonSerializer.Deserialize<ClientConfig>(json, options) ?? new ClientConfig();
            }
            else
            {
                // 파일이 없으면 기본값으로 시작하고 저장 시 새 파일을 만든다.
                statusMessage = "설정 파일이 없어 기본값으로 시작합니다.";
            }
        }
        catch (Exception ex)
        {
            // 파싱 실패 시 기본값으로 진행하되 오류 메시지를 남긴다.
            config = new ClientConfig();
            statusMessage = $"설정 파일을 읽는 중 오류가 발생했습니다: {ex.Message}";
        }

        // 서버 엔드포인트가 비어 있으면 기본 객체를 생성해 널 참조를 방지한다.
        config.Server ??= new ServerEndpoint();

        var viewModel = new ServerSettingsViewModel(configPath, config)
        {
            Host = config.Server.Host,
            ControlPortText = config.Server.ControlPort.ToString(),
            ServerPublicKey = config.Server.ServerPublicKey,
            StatusMessage = statusMessage
        };

        return viewModel;
    }

    // 입력값을 검증한 뒤 설정 파일에 저장한다.
    public bool TrySave()
    {
        // 호스트는 비어 있으면 안 되므로 사전 검증을 수행한다.
        if (string.IsNullOrWhiteSpace(Host))
        {
            StatusMessage = "서버 호스트를 입력해 주세요.";
            return false;
        }

        // 포트는 숫자이며 TCP 포트 범위(1~65535)인지 확인한다.
        if (!int.TryParse(ControlPortText, out var port) || port < 1 || port > 65535)
        {
            StatusMessage = "포트는 1~65535 범위의 숫자여야 합니다.";
            return false;
        }

        // 검증을 통과한 값을 설정 모델에 반영한다.
        _config.Server ??= new ServerEndpoint();
        _config.Server.Host = Host.Trim();
        _config.Server.ControlPort = port;
        _config.Server.ServerPublicKey = (ServerPublicKey ?? string.Empty).Trim();

        try
        {
            // 기존 포맷과 맞추기 위해 camelCase로 저장한다.
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configPath, json);

            StatusMessage = "저장되었습니다.";
            return true;
        }
        catch (Exception ex)
        {
            // 저장 실패 시 사용자에게 원인을 알려 다시 시도할 수 있게 한다.
            StatusMessage = $"저장 중 오류가 발생했습니다: {ex.Message}";
            return false;
        }
    }
}
