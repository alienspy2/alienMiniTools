using System;
using MiniTCPTunnel.Shared.Compression;
using MiniTCPTunnel.Shared.Crypto;

namespace MiniTCPTunnel.Shared.Protocol;

// 압축(LZ4) -> 암호(AEAD) 순서로 보호하는 구현체이다.
public sealed class SecureFrameProtector : IFrameProtector, IDisposable
{
    private readonly ICompressionCodec _compression;
    private readonly AeadChaCha20Poly1305 _encryptCipher;
    private readonly AeadChaCha20Poly1305 _decryptCipher;
    private readonly NonceSequence _encryptNonce;
    private readonly NonceSequence _decryptNonce;
    private bool _disposed;

    private SecureFrameProtector(
        ICompressionCodec compression,
        byte[] encryptKey,
        byte[] decryptKey,
        byte[] encryptNonceBase,
        byte[] decryptNonceBase)
    {
        _compression = compression;
        _encryptCipher = new AeadChaCha20Poly1305(encryptKey);
        _decryptCipher = new AeadChaCha20Poly1305(decryptKey);
        _encryptNonce = new NonceSequence(encryptNonceBase);
        _decryptNonce = new NonceSequence(decryptNonceBase);
    }

    public static SecureFrameProtector CreateForClient(SessionKeys keys, ICompressionCodec? compression = null)
    {
        // 클라이언트는 C2S로 암호화하고, S2C로 복호화한다.
        return new SecureFrameProtector(
            compression ?? new Lz4CompressionCodec(),
            keys.KeyC2S,
            keys.KeyS2C,
            keys.NonceBaseC2S,
            keys.NonceBaseS2C);
    }

    public static SecureFrameProtector CreateForServer(SessionKeys keys, ICompressionCodec? compression = null)
    {
        // 서버는 S2C로 암호화하고, C2S로 복호화한다.
        return new SecureFrameProtector(
            compression ?? new Lz4CompressionCodec(),
            keys.KeyS2C,
            keys.KeyC2S,
            keys.NonceBaseS2C,
            keys.NonceBaseC2S);
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var compressed = _compression.Compress(plaintext);
        var nonce = new byte[CryptoConstants.NonceSize];
        _encryptNonce.Next(nonce);
        return _encryptCipher.Encrypt(compressed, nonce, ReadOnlySpan<byte>.Empty);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
    {
        var nonce = new byte[CryptoConstants.NonceSize];
        _decryptNonce.Next(nonce);
        var compressed = _decryptCipher.Decrypt(ciphertext, nonce, ReadOnlySpan<byte>.Empty);
        return _compression.Decompress(compressed);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _encryptCipher.Dispose();
        _decryptCipher.Dispose();
        _disposed = true;
    }
}
