using System;
using System.IO;
using System.Text;

namespace MiniTCPTunnel.Shared.Crypto;

// 핸드셰이크에서 사용하는 Hello 메시지 직렬화 규격이다.
public sealed record HelloMessage
{
    public ushort ProtocolVersion { get; }
    public HandshakeRole Role { get; }
    public byte[] IdentityPublicKey { get; }
    public byte[] EphemeralPublicKey { get; }
    public byte[] Nonce { get; }
    public byte[] Signature { get; }

    public HelloMessage(
        ushort protocolVersion,
        HandshakeRole role,
        byte[] identityPublicKey,
        byte[] ephemeralPublicKey,
        byte[] nonce,
        byte[] signature)
    {
        ProtocolVersion = protocolVersion;
        Role = role;
        IdentityPublicKey = ValidateFixedLength(identityPublicKey, CryptoConstants.Ed25519PublicKeySize, nameof(identityPublicKey));
        EphemeralPublicKey = ValidateFixedLength(ephemeralPublicKey, CryptoConstants.X25519PublicKeySize, nameof(ephemeralPublicKey));
        Nonce = ValidateFixedLength(nonce, CryptoConstants.HelloNonceSize, nameof(nonce));
        Signature = ValidateFixedLength(signature, CryptoConstants.SignatureSize, nameof(signature));
    }

    public static HelloMessage CreateUnsigned(
        ushort protocolVersion,
        HandshakeRole role,
        byte[] identityPublicKey,
        byte[] ephemeralPublicKey,
        byte[] nonce)
    {
        // 서명 전용 인스턴스는 signature를 0으로 채운다.
        return new HelloMessage(
            protocolVersion,
            role,
            identityPublicKey,
            ephemeralPublicKey,
            nonce,
            new byte[CryptoConstants.SignatureSize]);
    }

    public HelloMessage WithSignature(byte[] signature)
    {
        return new HelloMessage(ProtocolVersion, Role, IdentityPublicKey, EphemeralPublicKey, Nonce, signature);
    }

    public byte[] GetSignedPayload()
    {
        return WriteTo(includeSignature: false);
    }

    public byte[] ToBytes()
    {
        return WriteTo(includeSignature: true);
    }

    public static HelloMessage FromBytes(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var protocolVersion = reader.ReadUInt16();
        var role = (HandshakeRole)reader.ReadByte();
        var identity = ReadBytes(reader);
        var ephemeral = ReadBytes(reader);
        var nonce = ReadBytes(reader);
        var signature = ReadBytes(reader);

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Hello 메시지에 예상치 못한 추가 데이터가 있습니다.");
        }

        return new HelloMessage(protocolVersion, role, identity, ephemeral, nonce, signature);
    }

    private byte[] WriteTo(bool includeSignature)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(ProtocolVersion);
        writer.Write((byte)Role);
        WriteBytes(writer, IdentityPublicKey);
        WriteBytes(writer, EphemeralPublicKey);
        WriteBytes(writer, Nonce);

        if (includeSignature)
        {
            WriteBytes(writer, Signature);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteBytes(BinaryWriter writer, byte[] value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Length > ushort.MaxValue)
        {
            throw new InvalidDataException("필드 길이가 ushort 범위를 초과했습니다.");
        }

        writer.Write((ushort)value.Length);
        writer.Write(value);
    }

    private static byte[] ReadBytes(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        var value = reader.ReadBytes(length);
        if (value.Length != length)
        {
            throw new EndOfStreamException("필드 데이터를 끝까지 읽지 못했습니다.");
        }

        return value;
    }

    private static byte[] ValidateFixedLength(byte[] value, int expectedLength, string name)
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
