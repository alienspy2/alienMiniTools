using System;

namespace MiniTCPTunnel.Shared.Protocol;

// 핸드셰이크 등 평문 구간에서 사용하는 패스스루 구현체이다.
public sealed class NullFrameProtector : IFrameProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        return plaintext.ToArray();
    }

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
    {
        return ciphertext.ToArray();
    }
}
