using System;
using System.Security.Cryptography;
using System.Text;

namespace MiniTCPTunnel.Shared.Crypto;

// HKDF-SHA256으로 세션 키/nonce_base를 파생한다.
public static class KeyDerivation
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("MiniTCPTunnel v1 session");

    public static SessionKeys DeriveSessionKeys(ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> transcriptHash)
    {
        var outputLength = (CryptoConstants.AeadKeySize * 2) + (CryptoConstants.NonceBaseSize * 2);

        // HKDF API는 outputLength를 세 번째 인자로 받는다.
        var okm = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret.ToArray(),
            outputLength,
            salt: transcriptHash.ToArray(),
            info: Info);

        return SessionKeys.FromOkm(okm);
    }
}
