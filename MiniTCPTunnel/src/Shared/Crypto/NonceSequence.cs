using System;
using System.Buffers.Binary;

namespace MiniTCPTunnel.Shared.Crypto;

// nonce_base(4바이트) + counter(8바이트) 형태의 nonce를 순차 생성한다.
public sealed class NonceSequence
{
    private readonly byte[] _nonceBase;
    private ulong _counter;

    public NonceSequence(byte[] nonceBase, ulong initialCounter = 0)
    {
        if (nonceBase is null)
        {
            throw new ArgumentNullException(nameof(nonceBase));
        }

        if (nonceBase.Length != CryptoConstants.NonceBaseSize)
        {
            throw new ArgumentException($"nonce_base 길이는 {CryptoConstants.NonceBaseSize}바이트여야 합니다.", nameof(nonceBase));
        }

        _nonceBase = nonceBase.ToArray();
        _counter = initialCounter;
    }

    public void Next(Span<byte> destination)
    {
        if (destination.Length != CryptoConstants.NonceSize)
        {
            throw new ArgumentException($"nonce 길이는 {CryptoConstants.NonceSize}바이트여야 합니다.", nameof(destination));
        }

        if (_counter == ulong.MaxValue)
        {
            throw new InvalidOperationException("nonce 카운터가 오버플로되었습니다.");
        }

        _nonceBase.CopyTo(destination);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(CryptoConstants.NonceBaseSize), _counter);
        _counter++;
    }
}
