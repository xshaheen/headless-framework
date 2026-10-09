// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging.Internal;

/// <summary>
/// A read-only view of a stream that libvips reads through, which the owner cuts off once the operation is done.
/// </summary>
/// <remarks>
/// libvips' operation cache can keep an operation, and the source it read from, alive after the call returns.
/// NetVips' stream source holds the .NET stream it was given, so without this indirection a cached operation would
/// keep the caller's stream, and a <see cref="MemoryStream" />'s whole buffer, reachable until the cache evicts it.
/// Detaching leaves the cache holding this empty shell instead. No cached operation reads it again: each call builds a
/// new source, so a cache hit never reuses one.
/// </remarks>
internal sealed class DetachableReadStream(Stream inner) : Stream
{
#pragma warning disable CA2213 // False positive: the view borrows the caller's stream and must leave it open.
    private Stream? _inner = inner;
#pragma warning restore CA2213

    public override bool CanRead => _inner is not null;

    public override bool CanSeek => _inner?.CanSeek ?? false;

    public override bool CanWrite => false;

    public override long Length => Inner.Length;

    public override long Position
    {
        get => Inner.Position;
        set => Inner.Position = value;
    }

    private Stream Inner => _inner ?? throw new ObjectDisposedException(nameof(DetachableReadStream));

    /// <summary>Cuts the view off from the underlying stream, which it never owned or disposes.</summary>
    public void Detach()
    {
        _inner = null;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        return Inner.Read(buffer);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return Inner.Seek(offset, origin);
    }

    public override void Flush() { }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        _inner = null;
        base.Dispose(disposing);
    }
}
