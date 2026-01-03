using System.Collections.Generic;

namespace MiniTCPTunnel.Shared.Config;

// 서버 측 설정 파일 구조를 표현한다.
public sealed class ServerConfig
{
    public int ControlPort { get; set; } = 9000;
    public string ControlListenHost { get; set; } = "0.0.0.0";
    public List<string> AllowedClientKeys { get; set; } = new();
}
