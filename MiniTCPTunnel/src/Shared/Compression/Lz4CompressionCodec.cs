using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using K4os.Compression.LZ4;

namespace MiniTCPTunnel.Shared.Compression;

// LZ4 압축 결과 앞에 원문 길이를 붙여 복원 시 길이를 보장한다.
public sealed class Lz4CompressionCodec : ICompressionCodec
{
    public byte[] Compress(ReadOnlySpan<byte> plaintext)
    {
        var source = plaintext.ToArray();
        var maxSize = LZ4Codec.MaximumOutputSize(source.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxSize);

        try
        {
            var encoded = LZ4Codec.Encode(source, 0, source.Length, rented, 0, maxSize);
            if (encoded <= 0)
            {
                throw new InvalidDataException("LZ4 압축에 실패했습니다.");
            }

            var output = new byte[4 + encoded];
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), (uint)source.Length);
            rented.AsSpan(0, encoded).CopyTo(output.AsSpan(4));
            return output;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length < 4)
        {
            throw new InvalidDataException("압축 데이터가 너무 짧아 원문 길이를 읽을 수 없습니다.");
        }

        var originalLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressed.Slice(0, 4));
        if (originalLength < 0)
        {
            throw new InvalidDataException("원문 길이가 음수입니다.");
        }

        var output = new byte[originalLength];
        var compressedPayload = compressed.Slice(4).ToArray();
        var decoded = LZ4Codec.Decode(compressedPayload, 0, compressedPayload.Length, output, 0, output.Length);

        if (decoded != originalLength)
        {
            throw new InvalidDataException("LZ4 복원 길이가 예상과 다릅니다.");
        }

        return output;
    }
}
