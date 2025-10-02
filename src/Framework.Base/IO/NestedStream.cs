// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Core;
using Microsoft.VisualBasic;

namespace Framework.IO;

/// <summary>A stream that allows for reading from another stream up to a given number of bytes.</summary>
/// <remarks>
/// Copy of<a href="https://github.com/dotnet/Nerdbank.Streams/blob/main/src/Nerdbank.Streams/NestedStream.cs"></a>
/// </remarks>
internal sealed class NestedStream : Stream, IHasIsDisposed
{
    /// <summary>The stream to read from.</summary>
#pragma warning disable CA2213 // The underlying stream is not owned by this instance. Do not dispose it.
    private readonly Stream _underlyingStream;
#pragma warning restore CA2213

    /// <summary>The total length of the stream.</summary>
    private readonly long _length;

    /// <summary>The remaining bytes allowed to be read.</summary>
    private long _remainingBytes;

    /// <summary>Initializes a new instance of the <see cref="NestedStream"/> class.</summary>
    /// <param name="underlyingStream">The stream to read from.</param>
    /// <param name="length">The number of bytes to read from the parent stream.</param>
    public NestedStream(Stream underlyingStream, long length)
    {
        Argument.IsNotNull(underlyingStream);
        Argument.CanRead(underlyingStream);
        Argument.IsPositiveOrZero(length);

        _underlyingStream = underlyingStream;
        _remainingBytes = length;
        _length = length;
    }

    public bool IsDisposed { get; private set; }

    public override bool CanRead => !IsDisposed;

    public override bool CanSeek => !IsDisposed && _underlyingStream.CanSeek;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            Ensure.NotDisposed(IsDisposed, this);

            return _underlyingStream.CanSeek ? _length : throw new NotSupportedException();
        }
    }

    public override long Position
    {
        get
        {
            Ensure.NotDisposed(IsDisposed, this);

            return _length - _remainingBytes;
        }
        set => Seek(value, SeekOrigin.Begin);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(buffer);
        Argument.IsPositiveOrZero(count);
        Argument.IsPositiveOrZero(offset);

        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Invalid offset and count combination.", nameof(count));
        }

        Ensure.NotDisposed(IsDisposed, this);

        count = (int)Math.Min(count, _remainingBytes);

        if (count <= 0)
        {
            return 0;
        }

        var bytesRead = await _underlyingStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

        _remainingBytes -= bytesRead;

        return bytesRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Argument.IsNotNull(buffer);
        Argument.IsPositiveOrZero(count);
        Argument.IsPositiveOrZero(offset);

        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Invalid offset and count combination.", nameof(count));
        }

        Ensure.NotDisposed(IsDisposed, this);

        count = (int)Math.Min(count, _remainingBytes);

        if (count <= 0)
        {
            return 0;
        }

        var bytesRead = _underlyingStream.Read(buffer, offset, count);
        _remainingBytes -= bytesRead;

        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Ensure.NotDisposed(IsDisposed, this);

        // If we're beyond the end of the stream (as the result of a Seek operation), return 0 bytes.
        if (_remainingBytes < 0)
        {
            return 0;
        }

        buffer = buffer[..(int)Math.Min(buffer.Length, _remainingBytes)];

        if (buffer.IsEmpty)
        {
            return 0;
        }

        var bytesRead = await _underlyingStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _remainingBytes -= bytesRead;

        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Ensure.NotDisposed(IsDisposed, this);

        if (!CanSeek)
        {
            throw new NotSupportedException("The underlying stream does not support seeking.");
        }

        // Recalculate offset relative to the current position
        var newOffset = origin switch
        {
            SeekOrigin.Current => offset,
            SeekOrigin.End => _length + offset - Position,
            SeekOrigin.Begin => offset - Position,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), "Invalid seek origin."),
        };

        // Determine whether the requested position is within the bounds of the stream
        if (Position + newOffset < 0)
        {
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");
        }

        var currentPosition = _underlyingStream.Position;
        var newPosition = _underlyingStream.Seek(newOffset, SeekOrigin.Current);
        _remainingBytes -= newPosition - currentPosition;

        return Position;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    #region Not Supported

    public override void SetLength(long value)
    {
        _ThrowDisposedOrNotSupported();
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _ThrowDisposedOrNotSupported();

        return Task.CompletedTask;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _ThrowDisposedOrNotSupported();
    }

    public override void Write(ReadOnlySpan<byte> buffer) => _ThrowDisposedOrNotSupported();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _ThrowDisposedOrNotSupported();

        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
        _ThrowDisposedOrNotSupported();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        _ThrowDisposedOrNotSupported();

        return Task.CompletedTask;
    }

    [DoesNotReturn]
    private void _ThrowDisposedOrNotSupported()
    {
        Ensure.NotDisposed(IsDisposed, this);

        throw new NotSupportedException();
    }

    #endregion
}
