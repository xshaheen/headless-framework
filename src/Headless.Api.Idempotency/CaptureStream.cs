// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

/// <summary>
/// Tee-style stream decorator: forwards all writes to the inner stream and simultaneously
/// captures bytes in an internal buffer up to a configurable cap.
/// The inner stream (the original <c>HttpResponse.Body</c>) is NOT disposed by this class.
/// </summary>
/// <remarks>
/// This stream is forward-only (write-only). <see cref="CanRead"/> and <see cref="CanSeek"/>
/// always return <see langword="false"/>; attempting <see cref="Read"/>,
/// <see cref="Seek"/>, <see cref="SetLength"/>, or accessing <see cref="Length"/> or
/// <see cref="Position"/> throws <see cref="NotSupportedException"/>. Write methods throw
/// <see cref="ObjectDisposedException"/> after <see cref="Dispose"/> is called.
/// </remarks>
internal sealed class CaptureStream : Stream
{
#pragma warning disable CA2213 // inner is not owned — the response pipeline owns it
    private readonly Stream _inner;
#pragma warning restore CA2213
    private readonly MemoryStream _buffer;
    private readonly int _cap;
    private bool _disposed;

    internal CaptureStream(Stream inner, int cap)
    {
        _inner = inner;
        _cap = cap;
        _buffer = new MemoryStream(Math.Min(cap, 4096));
    }

    /// <summary>True when writes exceeded the cap; captured bytes are incomplete.</summary>
    public bool TruncatedCapture { get; private set; }

    /// <summary>All bytes captured so far (up to the configured cap).</summary>
    public byte[] CapturedBytes => _buffer.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is forward-only.</exception>
    public override long Length => throw new NotSupportedException("CaptureStream is forward-only.");

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is forward-only.</exception>
    public override long Position
    {
        get => throw new NotSupportedException("CaptureStream is forward-only.");
        set => throw new NotSupportedException("CaptureStream is forward-only.");
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Append to the in-memory capture before forwarding. The append is in-memory and
        // cannot throw; if the inner write later throws, the captured buffer still represents
        // exactly what the handler intended to send up to that point.
        _AppendToBuffer(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _AppendToBuffer(buffer);
        _inner.Write(buffer);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _AppendToBuffer(buffer.AsSpan(offset, count));
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _AppendToBuffer(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override void WriteByte(byte value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _AppendToBuffer(new ReadOnlySpan<byte>(in value));
        _inner.WriteByte(value);
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is write-only.</exception>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is forward-only.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is forward-only.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _buffer.Dispose();
            _disposed = true;
        }

        base.Dispose(disposing);
        // _inner is intentionally NOT disposed — the response pipeline owns it
    }

    private void _AppendToBuffer(ReadOnlySpan<byte> data)
    {
        if (TruncatedCapture || data.IsEmpty)
        {
            return;
        }

        var remaining = _cap - (int)_buffer.Length;

        if (remaining <= 0)
        {
            TruncatedCapture = true;
            return;
        }

        var toWrite = Math.Min(remaining, data.Length);
        _buffer.Write(data[..toWrite]);

        if (toWrite < data.Length)
        {
            TruncatedCapture = true;
        }
    }
}
