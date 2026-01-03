namespace MiniTCPTunnel.Shared.Protocol;

public static class ProtocolConstants
{
    // 프로토콜 버전은 핸드셰이크에서 상호 호환성 확인에 사용된다.
    public const ushort ProtocolVersion = 1;

    // 프레임 길이(prefix)는 u32 LE로 고정한다.
    public const int LengthPrefixSize = 4;

    // type(1) + flags(1) + stream_id(4)
    public const int MinPlaintextSize = 1 + 1 + 4;

    // 비정상적인 과대 프레임을 막기 위한 상한선이다.
    public const int MaxFrameSize = 4 * 1024 * 1024;
}
