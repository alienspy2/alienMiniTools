namespace MiniTCPTunnel.Shared.Config;

// 하나의 터널 구성을 나타낸다.
public sealed class TunnelDefinition
{
    public string Id { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string LocalHost { get; set; } = "127.0.0.1";
    public int LocalPort { get; set; }
    public bool Enabled { get; set; } = true;
}
