using System;

namespace MiniTCPTunnel.Shared.Protocol;

// 프레임 보호(압축/암호)를 캡슐화해 읽기/쓰기 경로에서 동일하게 사용한다.
public interface IFrameProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}
