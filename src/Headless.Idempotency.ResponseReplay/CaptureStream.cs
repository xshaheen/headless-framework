// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.Idempotency;

/// <summary>
/// Tee-style stream decorator: forwards all writes to the inner stream and simultaneously
/// captures bytes in a pooled buffer up to a configurable cap.
/// The inner stream (the original <c>HttpResponse.Body</c>) is NOT disposed by this class.
/// </summary>
/// <remarks>
/// <para>
/// This stream is forward-only (write-only). <see cref="CanRead"/> and <see cref="CanSeek"/>
/// always return <see langword="false"/>; attempting <see cref="Read"/>,
/// <see cref="Seek"/>, <see cref="SetLength"/>, or accessing <see cref="Length"/> or
/// <see cref="Position"/> throws <see cref="NotSupportedException"/>. Write methods throw
/// <see cref="ObjectDisposedException"/> after <see cref="Dispose"/> is called.
/// </para>
/// <para>
/// The capture rents its backing array from <see cref="ArrayPool{T}.Shared"/> and returns it on
/// <see cref="Dispose"/> (zeroed, so a response body does not linger in pool memory), so capturing a
/// response allocates nothing beyond the pool rental; growth copies into a new rental instead of
/// leaving a chain of abandoned buffers.
/// </para>
/// </remarks>
internal sealed class CaptureStream : Stream
{
#pragma warning disable CA2213 // inner is not owned — the response pipeline owns it
    private readonly Stream _inner;
#pragma warning restore CA2213
    private readonly int _cap;
    private byte[] _buffer;
    private int _length;
    private bool _disposed;

    internal CaptureStream(Stream inner, int cap)
        : this(inner, cap, ArrayPool<byte>.Shared.Rent(Math.Min(cap, 4096))) { }

    /// <summary>Test seam: captures into the given rental, which <see cref="Dispose"/> returns to the pool.</summary>
    internal CaptureStream(Stream inner, int cap, byte[] rentedBuffer)
    {
        _inner = inner;
        _cap = cap;
        _buffer = rentedBuffer;
    }

    /// <summary>True when writes exceeded the cap; captured bytes are incomplete.</summary>
    public bool TruncatedCapture { get; private set; }

    /// <summary>All bytes captured so far (up to the configured cap). The span is valid until the next write.</summary>
    public ReadOnlySpan<byte> CapturedSpan => _buffer.AsSpan(0, _length);

    /// <summary>The bytes captured so far, copied into a fresh array.</summary>
    public byte[] CapturedBytes => _buffer.AsSpan(0, _length).ToArray();

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
        Ensure.NotDisposed(_disposed, this);
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
        Ensure.NotDisposed(_disposed, this);
        _AppendToBuffer(buffer);
        _inner.Write(buffer);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Ensure.NotDisposed(_disposed, this);
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
        Ensure.NotDisposed(_disposed, this);
        _AppendToBuffer(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public override void WriteByte(byte value)
    {
        Ensure.NotDisposed(_disposed, this);
        _AppendToBuffer(new ReadOnlySpan<byte>(in value));
        _inner.WriteByte(value);
    }

    public override void Flush()
    {
        _inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _inner.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is write-only.</exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is forward-only.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always — this stream is forward-only.</exception>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            // The captured response body can carry PII; zero it before the rental rejoins the shared pool so a
            // later renter cannot over-read residual bytes.
            _buffer.AsSpan(0, _length).Clear();
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
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

        var remaining = _cap - _length;

        if (remaining <= 0)
        {
            TruncatedCapture = true;
            return;
        }

        var toWrite = Math.Min(remaining, data.Length);

        if (toWrite > _buffer.Length - _length)
        {
            _Grow(_length + toWrite);
        }

        data[..toWrite].CopyTo(_buffer.AsSpan(_length));
        _length += toWrite;

        if (toWrite < data.Length)
        {
            TruncatedCapture = true;
        }
    }

    private void _Grow(int needed)
    {
        // Grow geometrically (at least double) so a long response does not copy per chunk, and never past the cap.
        var newSize = Math.Min(Math.Max(_buffer.Length * 2, needed), _cap);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);

        _buffer.AsSpan(0, _length).CopyTo(newBuffer);
        _buffer.AsSpan(0, _length).Clear();
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
