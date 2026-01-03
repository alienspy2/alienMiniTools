using System;

namespace MiniTCPTunnel.Shared.Protocol;

// 프로토콜 규격 위반이나 파싱 오류를 명확히 구분하기 위한 예외이다.
public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message)
    {
    }

    public ProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
