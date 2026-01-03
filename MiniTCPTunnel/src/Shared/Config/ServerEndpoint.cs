namespace MiniTCPTunnel.Shared.Config;

// 클라이언트가 접속할 서버 엔드포인트 정보이다.
public sealed class ServerEndpoint
{
    public string Host { get; set; } = "localhost";
    public int ControlPort { get; set; } = 9000;
    public string ServerPublicKey { get; set; } = string.Empty;
}
