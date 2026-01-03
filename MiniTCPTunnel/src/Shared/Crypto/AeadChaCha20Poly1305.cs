using System;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace MiniTCPTunnel.Shared.Crypto;

// NSec의 ChaCha20-Poly1305를 감싼 간단한 래퍼이다.
public sealed class AeadChaCha20Poly1305 : IDisposable
{
    private static readonly AeadAlgorithm Algorithm = AeadAlgorithm.ChaCha20Poly1305;
    private readonly Key _key;
    private bool _disposed;

    public AeadChaCha20Poly1305(ReadOnlySpan<byte> key)
    {
        if (key.Length != CryptoConstants.AeadKeySize)
        {
            throw new ArgumentException($"키 길이는 {CryptoConstants.AeadKeySize}바이트여야 합니다.", nameof(key));
        }

        _key = Key.Import(Algorithm, key.ToArray(), KeyBlobFormat.RawSymmetricKey);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad)
    {
        if (nonce.Length != CryptoConstants.NonceSize)
        {
            throw new ArgumentException($"nonce 길이는 {CryptoConstants.NonceSize}바이트여야 합니다.", nameof(nonce));
        }

        return Algorithm.Encrypt(_key, nonce, aad, plaintext);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad)
    {
        if (nonce.Length != CryptoConstants.NonceSize)
        {
            throw new ArgumentException($"nonce 길이는 {CryptoConstants.NonceSize}바이트여야 합니다.", nameof(nonce));
        }

        var plaintext = Algorithm.Decrypt(_key, nonce, aad, ciphertext);
        if (plaintext is null)
        {
            throw new CryptographicException("AEAD 인증에 실패했습니다.");
        }

        return plaintext;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _key.Dispose();
        _disposed = true;
    }
}
