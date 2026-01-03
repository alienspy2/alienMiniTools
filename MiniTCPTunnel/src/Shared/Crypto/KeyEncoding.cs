using System;
using System.IO;

namespace MiniTCPTunnel.Shared.Crypto;

// Base64 키 문자열을 길이 검증과 함께 변환한다.
public static class KeyEncoding
{
    public static byte[] DecodeBase64(string base64, int expectedLength, string label)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidDataException($"{label} 값이 비어 있습니다.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"{label} Base64 형식이 올바르지 않습니다.", ex);
        }

        if (bytes.Length != expectedLength)
        {
            throw new InvalidDataException($"{label} 길이가 올바르지 않습니다: {bytes.Length}");
        }

        return bytes;
    }

    public static string EncodeBase64(byte[] bytes)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        return Convert.ToBase64String(bytes);
    }
}
