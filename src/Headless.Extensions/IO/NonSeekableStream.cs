// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Core;

namespace Headless.IO;

/// <summary>
/// A <see cref="Stream"/> decorator that wraps an inner <paramref name="stream"/> and reports it as non-seekable,
/// forcing consumers down sequential read/write paths. Reading, writing, flushing, and closing are forwarded to the
/// inner stream, while all seek-related members (<see cref="Seek"/>, <see cref="SetLength"/>, <see cref="Length"/>,
/// and the <see cref="Position"/> setter) are unsupported.
/// </summary>
/// <param name="stream">The inner stream that read, write, flush, and close operations are delegated to.</param>
#pragma warning disable CA1065 // Do not raise exceptions in property getters
[PublicAPI]
public sealed class NonSeekableStream(Stream stream) : Stream, IHasIsDisposed
{
    /// <summary>Gets a value indicating whether this stream has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Gets a value indicating whether the inner stream supports reading.</summary>
    public override bool CanRead => stream.CanRead;

    /// <summary>Gets a value indicating whether this stream supports seeking. Always <see langword="false"/>.</summary>
    public override bool CanSeek => false;

    /// <summary>Gets a value indicating whether the inner stream supports writing.</summary>
    public override bool CanWrite => stream.CanWrite;

    /// <summary>This member is not supported because the stream is non-seekable.</summary>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed (seeking is never supported).</exception>
    public override long Length => throw _ThrowDisposedOrNotSupported();

    /// <summary>Gets the position within the inner stream. The setter is not supported because the stream is non-seekable.</summary>
    /// <exception cref="ObjectDisposedException">Thrown by the setter when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown by the setter when this stream has not been disposed (seeking is never supported).</exception>
    public override long Position
    {
        get => stream.Position;
        set => throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>Flushes any buffered data of the inner stream to its backing store.</summary>
    public override void Flush()
    {
        stream.Flush();
    }

    /// <summary>Reads a sequence of bytes from the inner stream into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The buffer into which the data is read.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, or <c>0</c> at the end of the stream.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return stream.Read(buffer, offset, count);
    }

    /// <summary>Writes a sequence of bytes from <paramref name="buffer"/> to the inner stream.</summary>
    /// <param name="buffer">The buffer containing the data to write.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin copying bytes.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        stream.Write(buffer, offset, count);
    }

    /// <summary>Closes the inner stream and then this stream, releasing all associated resources.</summary>
    public override void Close()
    {
        stream.Close();
        base.Close();
    }

    /// <summary>
    /// Releases the resources used by the stream. On first disposal, marks the stream as disposed and disposes the
    /// inner stream; subsequent calls are no-ops.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only
    /// unmanaged resources.
    /// </param>
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        stream.Dispose();
        base.Dispose(disposing);
    }

    #region Not Supported

    /// <summary>This member is not supported because the stream is non-seekable.</summary>
    /// <param name="offset">Unused.</param>
    /// <param name="origin">Unused.</param>
    /// <returns>This method never returns; it always throws.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed (seeking is never supported).</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    /// <summary>This member is not supported because the stream is non-seekable.</summary>
    /// <param name="value">Unused.</param>
    /// <exception cref="ObjectDisposedException">Thrown when this stream has been disposed.</exception>
    /// <exception cref="NotSupportedException">Thrown when this stream has not been disposed (changing the length is never supported).</exception>
    public override void SetLength(long value)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    [DoesNotReturn]
    private Exception _ThrowDisposedOrNotSupported()
    {
        Ensure.NotDisposed(IsDisposed, this);

        throw new NotSupportedException();
    }

    #endregion
}
