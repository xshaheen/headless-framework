// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Core;

namespace Headless.IO;

/// <summary>
/// A forward-only, read-only <see cref="Stream"/> decorator that throws when more than a configured number of bytes
/// are read. Unlike a bounded slice, this stream probes one byte beyond the boundary so oversized input cannot appear
/// to end cleanly at the limit.
/// </summary>
/// <remarks>
/// Each inner read is capped to at most one byte beyond the remaining allowance. This detects overflow without
/// consuming an arbitrarily large caller buffer from the inner stream.
/// </remarks>
#pragma warning disable CA1065 // Stream requires unsupported Length and Position property members.
[PublicAPI]
public sealed class SizeLimitedReadStream : Stream, IHasIsDisposed
{
#pragma warning disable CA2213 // Ownership is controlled by the leaveOpen constructor argument.
    private readonly Stream _stream;
#pragma warning restore CA2213
    private readonly bool _leaveOpen;
    private bool _limitExceeded;

    /// <summary>Initializes a new instance of the <see cref="SizeLimitedReadStream"/> class.</summary>
    /// <param name="stream">The readable stream to decorate.</param>
    /// <param name="maximumBytes">The maximum number of bytes that may be read before an exception is thrown.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="stream"/> open when this stream is disposed; otherwise,
    /// <see langword="false"/>. The default is <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> does not support reading.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumBytes"/> is negative.</exception>
    public SizeLimitedReadStream(Stream stream, long maximumBytes, bool leaveOpen = false)
    {
        Argument.IsNotNull(stream);
        Argument.CanRead(stream);
        Argument.IsPositiveOrZero(maximumBytes);

        _stream = stream;
        MaximumBytes = maximumBytes;
        _leaveOpen = leaveOpen;
    }

    /// <summary>Gets the maximum number of bytes that may be read.</summary>
    public long MaximumBytes { get; }

    /// <summary>Gets the number of bytes observed from the inner stream.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Gets a value indicating whether this stream has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Gets a value indicating whether this stream supports reading.</summary>
    public override bool CanRead => !IsDisposed && _stream.CanRead;

    /// <summary>Gets a value indicating whether this stream supports seeking. Always <see langword="false"/>.</summary>
    public override bool CanSeek => false;

    /// <summary>Gets a value indicating whether this stream supports writing. Always <see langword="false"/>.</summary>
    public override bool CanWrite => false;

    /// <summary>This member is not supported because the stream is non-seekable.</summary>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed.</exception>
    public override long Length => throw _ThrowDisposedOrNotSupported();

    /// <summary>This member is not supported because the stream is non-seekable.</summary>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed.</exception>
    public override long Position
    {
        get => throw _ThrowDisposedOrNotSupported();
        set => throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>Reads bytes from the inner stream and enforces the configured byte limit.</summary>
    /// <param name="buffer">The buffer into which the data is read.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, or <c>0</c> at the end of the stream.</returns>
    /// <exception cref="StreamSizeLimitExceededException">Thrown when the total bytes read exceed <see cref="MaximumBytes"/>.</exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    /// <summary>Reads bytes from the inner stream and enforces the configured byte limit.</summary>
    /// <param name="buffer">The region of memory into which the data is read.</param>
    /// <returns>The number of bytes read, or <c>0</c> at the end of the stream.</returns>
    /// <exception cref="StreamSizeLimitExceededException">Thrown when the total bytes read exceed <see cref="MaximumBytes"/>.</exception>
    public override int Read(Span<byte> buffer)
    {
        _EnsureReadable();

        var read = _stream.Read(buffer[.._GetReadLength(buffer.Length)]);
        _Track(read);

        return read;
    }

    /// <summary>Asynchronously reads bytes from the inner stream and enforces the configured byte limit.</summary>
    /// <param name="buffer">The buffer into which the data is read.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the read to complete.</param>
    /// <returns>A task whose result is the number of bytes read, or <c>0</c> at the end of the stream.</returns>
    /// <exception cref="StreamSizeLimitExceededException">Thrown when the total bytes read exceed <see cref="MaximumBytes"/>.</exception>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <summary>Asynchronously reads bytes from the inner stream and enforces the configured byte limit.</summary>
    /// <param name="buffer">The region of memory into which the data is read.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the read to complete.</param>
    /// <returns>A task whose result is the number of bytes read, or <c>0</c> at the end of the stream.</returns>
    /// <exception cref="StreamSizeLimitExceededException">Thrown when the total bytes read exceed <see cref="MaximumBytes"/>.</exception>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _EnsureReadable();

        var read = await _stream
            .ReadAsync(buffer[.._GetReadLength(buffer.Length)], cancellationToken)
            .ConfigureAwait(false);
        _Track(read);

        return read;
    }

    /// <summary>This member is not supported because the stream is read-only.</summary>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed.</exception>
    public override void Flush()
    {
        throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>This member is not supported because the stream is non-seekable.</summary>
    /// <param name="offset">A byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">A value indicating the reference point used to obtain the new position.</param>
    /// <returns>This method does not return.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>This member is not supported because the stream is read-only.</summary>
    /// <param name="value">The desired length of the stream.</param>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed.</exception>
    public override void SetLength(long value)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>This member is not supported because the stream is read-only.</summary>
    /// <param name="buffer">The buffer containing the data to write.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> from which to begin writing.</param>
    /// <param name="count">The number of bytes to write.</param>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed.</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>Releases this stream and, unless configured otherwise, the inner stream.</summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            IsDisposed = true;

            if (!_leaveOpen)
            {
                _stream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>Asynchronously releases this stream and, unless configured otherwise, the inner stream.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public override async ValueTask DisposeAsync()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;

            if (!_leaveOpen)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private int _GetReadLength(int requestedBytes)
    {
        var remainingBytes = MaximumBytes - BytesRead;
        if (remainingBytes >= requestedBytes)
        {
            return requestedBytes;
        }

        return (int)remainingBytes + 1;
    }

    private void _Track(int read)
    {
        BytesRead += read;
        if (BytesRead <= MaximumBytes)
        {
            return;
        }

        _limitExceeded = true;
        throw new StreamSizeLimitExceededException(MaximumBytes, BytesRead);
    }

    private void _EnsureReadable()
    {
        Ensure.NotDisposed(IsDisposed, this);

        if (_limitExceeded)
        {
            throw new StreamSizeLimitExceededException(MaximumBytes, BytesRead);
        }
    }

    [DoesNotReturn]
    private Exception _ThrowDisposedOrNotSupported()
    {
        Ensure.NotDisposed(IsDisposed, this);

        throw new NotSupportedException();
    }
}
