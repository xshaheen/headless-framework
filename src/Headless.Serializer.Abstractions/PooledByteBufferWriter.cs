// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Serializer;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by an <see cref="ArrayPool{T}"/> rental. Used by
/// <see cref="SerializerExtensions"/> to bridge the buffer-first <see cref="ISerializer"/> contract to the
/// <c>byte[]</c> / <c>string</c> / <see cref="Stream"/> convenience adapters without paying for a
/// <see cref="MemoryStream"/> plus its <c>ToArray()</c> copy, and available to consumers that want to serialize
/// into a pooled buffer and read the result via <see cref="WrittenSpan"/> / <see cref="WrittenMemory"/> with no
/// intermediate array. Always use inside a <c>using</c> block so the rented array returns to the pool; the written
/// span/memory is only valid until the next write or <see cref="Dispose"/>.
/// </summary>
[PublicAPI]
public sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int _DefaultInitialBufferSize = 256;

    private byte[] _buffer;
    private int _index;

    public PooledByteBufferWriter(int initialCapacity = _DefaultInitialBufferSize)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, _DefaultInitialBufferSize));
        _index = 0;
    }

    /// <summary>The bytes written so far. Valid only until the next write or <see cref="Dispose"/>.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _index);

    /// <summary>
    /// The bytes written so far, as memory backed by the pooled rental. Valid only until the next write or
    /// <see cref="Dispose"/> — do not retain it past the <c>using</c> scope.
    /// </summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _index);

    public void Advance(int count)
    {
        // The buffer-first serializers never advance past what GetSpan/GetMemory granted, so a defensive guard
        // here would only add a branch on the hot path; rely on the IBufferWriter contract instead.
        _index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        _EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        _EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_index);
    }

    public void Dispose()
    {
        var toReturn = _buffer;
        _buffer = [];
        _index = 0;

        if (toReturn.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(toReturn);
        }
    }

    private void _EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1)
        {
            sizeHint = 1;
        }

        if (sizeHint <= _buffer.Length - _index)
        {
            return;
        }

        // Grow geometrically (at least double) to amortize repeated small writes, but honor a large sizeHint.
        var newSize = Math.Max(_buffer.Length * 2, _buffer.Length + sizeHint);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);

        Array.Copy(_buffer, newBuffer, _index);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
