using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MiniTCPTunnel.Shared.Protocol;

// 길이 프리픽스 기반의 프레임 읽기/쓰기 스트림 래퍼이다.
public sealed class FrameStream
{
    private readonly Stream _stream;
    private readonly IFrameProtector _protector;

    public FrameStream(Stream stream, IFrameProtector protector)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async ValueTask WriteAsync(Frame frame, CancellationToken cancellationToken)
    {
        var plaintext = frame.ToPlainBytes();
        var ciphertext = _protector.Protect(plaintext);

        if (ciphertext.Length == 0 || ciphertext.Length > ProtocolConstants.MaxFrameSize)
        {
            throw new ProtocolException($"암호문 길이가 허용 범위를 벗어났습니다: {ciphertext.Length}");
        }

        var lengthPrefix = new byte[ProtocolConstants.LengthPrefixSize];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthPrefix, (uint)ciphertext.Length);

        await _stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Frame> ReadAsync(CancellationToken cancellationToken)
    {
        var lengthPrefix = await ReadExactlyAsync(ProtocolConstants.LengthPrefixSize, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthPrefix);

        if (length == 0 || length > ProtocolConstants.MaxFrameSize)
        {
            throw new ProtocolException($"프레임 길이가 허용 범위를 벗어났습니다: {length}");
        }

        var ciphertext = await ReadExactlyAsync((int)length, cancellationToken).ConfigureAwait(false);
        var plaintext = _protector.Unprotect(ciphertext);

        return Frame.FromPlainBytes(plaintext);
    }

    private async ValueTask<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("스트림이 예상보다 빨리 종료되었습니다.");
            }

            offset += read;
        }

        return buffer;
    }
}
