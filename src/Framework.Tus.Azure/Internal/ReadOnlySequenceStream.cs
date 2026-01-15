// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Framework.Tus.Internal;

/// <summary>
/// A read-only stream wrapper over a <see cref="ReadOnlySequence{T}"/> of bytes.
/// Enables zero-copy streaming of PipeReader buffers to APIs that require Stream.
/// </summary>
internal sealed class ReadOnlySequenceStream(ReadOnlySequence<byte> sequence) : Stream
{
    private SequencePosition _position = sequence.Start;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Length => sequence.Length;

    public override long Position
    {
        get => sequence.Slice(sequence.Start, _position).Length;
        set
        {
            if (value < 0 || value > sequence.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _position = sequence.GetPosition(value);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = sequence.Slice(_position);

        if (remaining.IsEmpty)
        {
            return 0;
        }

        var bytesToRead = (int)Math.Min(count, remaining.Length);
        var slice = remaining.Slice(0, bytesToRead);
        slice.CopyTo(buffer.AsSpan(offset, bytesToRead));
        _position = sequence.GetPosition(bytesToRead, _position);

        return bytesToRead;
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = sequence.Slice(_position);

        if (remaining.IsEmpty)
        {
            return 0;
        }

        var bytesToRead = (int)Math.Min(buffer.Length, remaining.Length);
        var slice = remaining.Slice(0, bytesToRead);
        slice.CopyTo(buffer[..bytesToRead]);
        _position = sequence.GetPosition(bytesToRead, _position);

        return bytesToRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => sequence.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        Position = newPosition;

        return newPosition;
    }

    public override void Flush()
    {
        // No-op for read-only stream
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
