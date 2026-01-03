using System.Collections.Generic;

namespace MiniTCPTunnel.Shared.Config;

// 클라이언트의 기본 설정을 보관한다.
public sealed class ClientConfig
{
    public ServerEndpoint Server { get; set; } = new();
    public List<TunnelDefinition> Tunnels { get; set; } = new();
    public string IdentityKeyPath { get; set; } = "client.key";
}
