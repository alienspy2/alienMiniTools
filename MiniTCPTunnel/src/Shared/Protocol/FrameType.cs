namespace MiniTCPTunnel.Shared.Protocol;

// 제어 채널과 데이터 채널 모두 동일한 타입 집합을 사용한다.
public enum FrameType : byte
{
    Hello = 1,
    AuthOk = 2,
    AuthFail = 3,

    OpenTunnel = 10,
    CloseTunnel = 11,
    TunnelStatus = 12,

    IncomingConn = 20,
    DataConnReady = 21,

    Heartbeat = 30,
    Error = 40,

    Data = 100
}
