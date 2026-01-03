using System;
using System.Security.Cryptography;

namespace MiniTCPTunnel.Shared.Crypto;

// 핸드셰이크 메시지의 해시를 만들어 HKDF salt로 사용한다.
public static class HandshakeTranscript
{
    public static byte[] ComputeHash(ReadOnlySpan<byte> clientHello, ReadOnlySpan<byte> serverHello)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(clientHello);
        hash.AppendData(serverHello);
        return hash.GetHashAndReset();
    }
}
