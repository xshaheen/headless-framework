// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.IO;

/// <summary>
/// A <see cref="Stream"/> decorator that wraps an inner <paramref name="stream"/> and invokes a user-supplied
/// <paramref name="disposeAction"/> exactly once when the stream is disposed (or closed), just before the inner
/// stream is disposed. All other operations are forwarded verbatim to the inner stream.
/// </summary>
/// <param name="stream">The inner stream that all operations are delegated to.</param>
/// <param name="disposeAction">
/// An action invoked once during disposal, before the inner stream is disposed. Any exception it raises is
/// swallowed so the inner stream is still disposed.
/// </param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
[PublicAPI]
public sealed class ActionableStream(Stream stream, Action disposeAction) : Stream
{
    private readonly Stream _stream = Argument.IsNotNull(stream);
    private bool _disposed;

    /// <summary>
    /// Releases the resources used by the stream. When <paramref name="disposing"/> is <see langword="true"/> and the
    /// stream has not already been disposed, invokes <c>disposeAction</c> (ignoring any exception it raises) and then
    /// disposes the inner stream. The finalizer path (<paramref name="disposing"/> is <see langword="false"/>) is a no-op
    /// beyond the base call, so the action and inner stream are never touched from the finalizer.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only
    /// unmanaged resources (finalizer path).
    /// </param>
    protected override void Dispose(bool disposing)
    {
        // Skip the finalizer path: the user disposeAction and inner stream are managed resources and must not be
        // touched once the GC is reclaiming us. Routing all disposal through Dispose(true) keeps the action firing
        // exactly once for both Dispose() and Close() (Stream.Close calls Dispose).
        if (!disposing)
        {
            base.Dispose(disposing);

            return;
        }

        if (_disposed)
        {
            base.Dispose(disposing);

            return;
        }

        _disposed = true;

        try
        {
            disposeAction.Invoke();
        }
#pragma warning disable ERP022
        catch
        {
            /* ignore if these are already disposed; this is to make sure they are */
        }
#pragma warning restore ERP022

        _stream.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>Gets a value indicating whether the inner stream supports reading.</summary>
    public override bool CanRead => _stream.CanRead;

    /// <summary>Gets a value indicating whether the inner stream supports seeking.</summary>
    public override bool CanSeek => _stream.CanSeek;

    /// <summary>Gets a value indicating whether the inner stream supports writing.</summary>
    public override bool CanWrite => _stream.CanWrite;

    /// <summary>Gets the length, in bytes, of the inner stream.</summary>
    public override long Length => _stream.Length;

    /// <summary>Gets a value indicating whether the inner stream can time out.</summary>
    public override bool CanTimeout => _stream.CanTimeout;

    /// <summary>Gets or sets the read timeout (in milliseconds) of the inner stream.</summary>
    public override int ReadTimeout
    {
        get => _stream.ReadTimeout;
        set => _stream.ReadTimeout = value;
    }

    /// <summary>Gets or sets the write timeout (in milliseconds) of the inner stream.</summary>
    public override int WriteTimeout
    {
        get => _stream.WriteTimeout;
        set => _stream.WriteTimeout = value;
    }

    /// <summary>Gets or sets the position within the inner stream.</summary>
    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    /// <summary>Sets the position within the inner stream.</summary>
    /// <param name="offset">A byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">A value indicating the reference point used to obtain the new position.</param>
    /// <returns>The new position within the inner stream.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    /// <summary>Sets the length of the inner stream.</summary>
    /// <param name="value">The desired length of the inner stream, in bytes.</param>
    public override void SetLength(long value)
    {
        _stream.SetLength(value);
    }

    /// <summary>Asynchronously reads the bytes from the inner stream and writes them to <paramref name="destination"/>.</summary>
    /// <param name="destination">The stream to which the contents of the inner stream are copied.</param>
    /// <param name="bufferSize">The size, in bytes, of the buffer used for copying.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the copy to complete.</param>
    /// <returns>A task that represents the asynchronous copy operation.</returns>
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return _stream.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    /// <summary>Flushes any buffered data of the inner stream to its backing store.</summary>
    public override void Flush()
    {
        _stream.Flush();
    }

    /// <summary>Asynchronously flushes any buffered data of the inner stream to its backing store.</summary>
    /// <param name="cancellationToken">A token to observe while waiting for the flush to complete.</param>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _stream.FlushAsync(cancellationToken);
    }

    /// <summary>Reads a sequence of bytes from the inner stream into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The buffer into which the data is read.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, or <c>0</c> at the end of the stream.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return _stream.Read(buffer, offset, count);
    }

    /// <summary>Reads a sequence of bytes from the inner stream into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The region of memory into which the data is read.</param>
    /// <returns>The number of bytes read, or <c>0</c> at the end of the stream.</returns>
    public override int Read(Span<byte> buffer)
    {
        return _stream.Read(buffer);
    }

    /// <summary>Asynchronously reads a sequence of bytes from the inner stream into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The buffer into which the data is read.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the read to complete.</param>
    /// <returns>A task whose result is the number of bytes read, or <c>0</c> at the end of the stream.</returns>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _stream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    /// <summary>Asynchronously reads a sequence of bytes from the inner stream into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The region of memory into which the data is read.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the read to complete.</param>
    /// <returns>A task whose result is the number of bytes read, or <c>0</c> at the end of the stream.</returns>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _stream.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>Writes a single byte to the inner stream at the current position.</summary>
    /// <param name="value">The byte to write.</param>
    public override void WriteByte(byte value)
    {
        _stream.WriteByte(value);
    }

    /// <summary>Writes a sequence of bytes from <paramref name="buffer"/> to the inner stream.</summary>
    /// <param name="buffer">The buffer containing the data to write.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin copying bytes.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        _stream.Write(buffer, offset, count);
    }

    /// <summary>Writes a sequence of bytes from <paramref name="buffer"/> to the inner stream.</summary>
    /// <param name="buffer">The region of memory containing the data to write.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _stream.Write(buffer);
    }

    /// <summary>Asynchronously writes a sequence of bytes from <paramref name="buffer"/> to the inner stream.</summary>
    /// <param name="buffer">The buffer containing the data to write.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin copying bytes.</param>
    /// <param name="count">The number of bytes to write.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the write to complete.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _stream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    /// <summary>Asynchronously writes a sequence of bytes from <paramref name="buffer"/> to the inner stream.</summary>
    /// <param name="buffer">The region of memory containing the data to write.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the write to complete.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _stream.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>Ends an asynchronous write operation on the inner stream.</summary>
    /// <param name="asyncResult">The reference to the pending asynchronous request to finish.</param>
    public override void EndWrite(IAsyncResult asyncResult)
    {
        _stream.EndWrite(asyncResult);
    }

    /// <summary>Reads a single byte from the inner stream at the current position.</summary>
    /// <returns>The byte read, cast to an <see cref="int"/>, or <c>-1</c> at the end of the stream.</returns>
    public override int ReadByte()
    {
        return _stream.ReadByte();
    }

    // No Close() override: the base Stream.Close() already routes to Dispose(true), which runs disposeAction
    // exactly once. Overriding Close() to call Dispose() would recurse infinitely (Stream.Dispose() calls Close()).

    /// <summary>Begins an asynchronous read operation on the inner stream.</summary>
    /// <param name="buffer">The buffer into which the data is read.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="callback">An optional callback invoked when the read completes.</param>
    /// <param name="state">A user-provided object that distinguishes this request from others.</param>
    /// <returns>An <see cref="IAsyncResult"/> representing the asynchronous read.</returns>
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return _stream.BeginRead(buffer, offset, count, callback, state);
    }

    /// <summary>Ends an asynchronous read operation on the inner stream.</summary>
    /// <param name="asyncResult">The reference to the pending asynchronous request to finish.</param>
    /// <returns>The number of bytes read, or <c>0</c> at the end of the stream.</returns>
    public override int EndRead(IAsyncResult asyncResult)
    {
        return _stream.EndRead(asyncResult);
    }

    /// <summary>Begins an asynchronous write operation on the inner stream.</summary>
    /// <param name="buffer">The buffer containing the data to write.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to begin copying bytes.</param>
    /// <param name="count">The number of bytes to write.</param>
    /// <param name="callback">An optional callback invoked when the write completes.</param>
    /// <param name="state">A user-provided object that distinguishes this request from others.</param>
    /// <returns>An <see cref="IAsyncResult"/> representing the asynchronous write.</returns>
    public override IAsyncResult BeginWrite(
        byte[] buffer,
        int offset,
        int count,
        AsyncCallback? callback,
        object? state
    )
    {
        return _stream.BeginWrite(buffer, offset, count, callback, state);
    }
}
