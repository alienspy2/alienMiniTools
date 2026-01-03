using System;

namespace MiniTCPTunnel.Shared.Crypto;

// 방향별 키/nonce_base를 묶어 관리한다.
public sealed record SessionKeys
{
    public byte[] KeyC2S { get; }
    public byte[] KeyS2C { get; }
    public byte[] NonceBaseC2S { get; }
    public byte[] NonceBaseS2C { get; }

    public SessionKeys(byte[] keyC2S, byte[] keyS2C, byte[] nonceBaseC2S, byte[] nonceBaseS2C)
    {
        KeyC2S = ValidateLength(keyC2S, CryptoConstants.AeadKeySize, nameof(keyC2S));
        KeyS2C = ValidateLength(keyS2C, CryptoConstants.AeadKeySize, nameof(keyS2C));
        NonceBaseC2S = ValidateLength(nonceBaseC2S, CryptoConstants.NonceBaseSize, nameof(nonceBaseC2S));
        NonceBaseS2C = ValidateLength(nonceBaseS2C, CryptoConstants.NonceBaseSize, nameof(nonceBaseS2C));
    }

    public static SessionKeys FromOkm(ReadOnlySpan<byte> okm)
    {
        var expected = (CryptoConstants.AeadKeySize * 2) + (CryptoConstants.NonceBaseSize * 2);
        if (okm.Length != expected)
        {
            throw new ArgumentException($"HKDF 출력 길이가 올바르지 않습니다: {okm.Length}");
        }

        var offset = 0;
        var keyC2S = okm.Slice(offset, CryptoConstants.AeadKeySize).ToArray();
        offset += CryptoConstants.AeadKeySize;
        var keyS2C = okm.Slice(offset, CryptoConstants.AeadKeySize).ToArray();
        offset += CryptoConstants.AeadKeySize;
        var nonceBaseC2S = okm.Slice(offset, CryptoConstants.NonceBaseSize).ToArray();
        offset += CryptoConstants.NonceBaseSize;
        var nonceBaseS2C = okm.Slice(offset, CryptoConstants.NonceBaseSize).ToArray();

        return new SessionKeys(keyC2S, keyS2C, nonceBaseC2S, nonceBaseS2C);
    }

    private static byte[] ValidateLength(byte[] value, int expectedLength, string name)
    {
        if (value is null)
        {
            throw new ArgumentNullException(name);
        }

        if (value.Length != expectedLength)
        {
            throw new ArgumentException($"{name} 길이는 {expectedLength}바이트여야 합니다.", name);
        }

        return value.ToArray();
    }
}
