using System;
using System.Buffers.Binary;

namespace MiniTCPTunnel.Shared.Protocol;

// 암호화/압축 전에 사용하는 평문 프레임 표현이다.
public sealed class Frame
{
    public FrameType Type { get; }
    public byte Flags { get; }
    public uint StreamId { get; }
    public byte[] Payload { get; }

    public Frame(FrameType type, byte flags, uint streamId, byte[] payload)
    {
        Type = type;
        Flags = flags;
        StreamId = streamId;
        Payload = payload ?? Array.Empty<byte>();
    }

    public byte[] ToPlainBytes()
    {
        // 평문 프레임은 고정 헤더 + payload로 구성한다.
        var buffer = new byte[ProtocolConstants.MinPlaintextSize + Payload.Length];
        buffer[0] = (byte)Type;
        buffer[1] = Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(2, 4), StreamId);
        Payload.CopyTo(buffer.AsSpan(6));
        return buffer;
    }

    public static Frame FromPlainBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < ProtocolConstants.MinPlaintextSize)
        {
            throw new ProtocolException($"평문 프레임 길이가 너무 짧습니다: {data.Length}");
        }

        var type = (FrameType)data[0];
        var flags = data[1];
        var streamId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2, 4));
        var payload = data.Slice(6).ToArray();

        return new Frame(type, flags, streamId, payload);
    }
}
