namespace MiniTCPTunnel.Shared.Crypto;

// 프로토콜 전반에서 사용하는 고정 크기 상수 모음이다.
public static class CryptoConstants
{
    public const int AeadKeySize = 32;
    public const int NonceSize = 12;
    public const int NonceBaseSize = 4;
    public const int NonceCounterSize = 8;

    public const int Ed25519PublicKeySize = 32;
    public const int X25519PublicKeySize = 32;
    public const int SignatureSize = 64;

    public const int HelloNonceSize = 16;
}
