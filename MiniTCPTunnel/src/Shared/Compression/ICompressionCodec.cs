using System;

namespace MiniTCPTunnel.Shared.Compression;

// 압축 알고리즘을 추상화해 테스트/교체가 가능하도록 한다.
public interface ICompressionCodec
{
    byte[] Compress(ReadOnlySpan<byte> plaintext);
    byte[] Decompress(ReadOnlySpan<byte> compressed);
}
