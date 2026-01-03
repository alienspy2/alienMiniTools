namespace MiniTCPTunnel.Shared.Crypto;

// 핸드셰이크에서 역할을 명시해 혼선을 방지한다.
public enum HandshakeRole : byte
{
    Client = 1,
    Server = 2
}
