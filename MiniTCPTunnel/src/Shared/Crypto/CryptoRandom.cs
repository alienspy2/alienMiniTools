using System.Security.Cryptography;

namespace MiniTCPTunnel.Shared.Crypto;

// 안전한 랜덤 바이트를 생성한다.
public static class CryptoRandom
{
    public static byte[] GenerateBytes(int length)
    {
        var buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }
}
