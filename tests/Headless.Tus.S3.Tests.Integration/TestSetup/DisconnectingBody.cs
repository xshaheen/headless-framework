// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace Tests.TestSetup;

/// <summary>
/// PATCH bodies that deliver some bytes and then behave as tusdotnet's guarded request body does when the client
/// disconnects: the request token is cancelled, then a <c>Stream</c> read returns 0, and a <c>PipeReader</c> read
/// returns the last bytes with <see cref="ReadResult.IsCanceled" /> set.
/// </summary>
internal static class DisconnectingBody
{
    private const int _ReadSize = 256 * 1024;

    /// <summary>A stream serving <paramref name="data" />, then disconnecting after <paramref name="deliver" /> bytes.</summary>
    public static Stream Stream(byte[] data, int deliver, CancellationTokenSource disconnect)
    {
        return new DisconnectingStream(data, deliver, disconnect);
    }

    /// <summary>A reader serving <paramref name="data" />, then disconnecting after <paramref name="deliver" /> bytes.</summary>
    public static PipeReader Pipe(byte[] data, int deliver, CancellationTokenSource disconnect)
    {
        return new DisconnectingPipeReader(data, deliver, disconnect);
    }

    private sealed class DisconnectingStream(byte[] data, int deliver, CancellationTokenSource disconnect) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_position >= deliver)
            {
                await disconnect.CancelAsync();

                return 0;
            }

            var count = Math.Min(Math.Min(buffer.Length, _ReadSize), deliver - _position);
            data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;

            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DisconnectingPipeReader(byte[] data, int deliver, CancellationTokenSource disconnect)
        : PipeReader
    {
        private int _position;

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var count = Math.Min(_ReadSize, deliver - _position);
            var last = _position + count >= deliver;

            if (last)
            {
                await disconnect.CancelAsync();
            }

            return new ReadResult(
                new ReadOnlySequence<byte>(data, _position, count),
                isCanceled: last,
                isCompleted: false
            );
        }

        public override bool TryRead(out ReadResult result)
        {
            throw new NotSupportedException();
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
            _position = consumed.GetInteger();
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            AdvanceTo(consumed);
        }

        public override void CancelPendingRead() { }

        public override void Complete(Exception? exception = null) { }
    }
}
