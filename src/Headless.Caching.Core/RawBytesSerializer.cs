// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;
using Headless.Serializer;

namespace Headless.Caching;

/// <summary>
/// Identity serializer for adapter-owned named cache instances — shared by the BCL <c>IDistributedCache</c>
/// adapter and the ASP.NET Core <c>IOutputCacheStore</c> adapter — whose values are already raw
/// <see cref="byte"/> arrays. Supports only <see cref="byte"/> arrays by construction (the adapter never
/// stores anything else) and throws for any other type.
/// </summary>
internal sealed class RawBytesSerializer : IBinarySerializer
{
    /// <inheritdoc />
    public T? Deserialize<T>(Stream data)
    {
        if (typeof(T) != typeof(byte[]))
        {
            throw _Unsupported(typeof(T));
        }

        return (T?)(object)_ReadAllBytes(data);
    }

    /// <inheritdoc />
    public void Serialize<T>(T value, Stream output)
    {
        Argument.IsNotNull(value);

        if (value is not byte[] bytes)
        {
            throw _Unsupported(typeof(T));
        }

        output.Write(bytes);
    }

    /// <inheritdoc />
    public object? Deserialize(Stream data, Type objectType)
    {
        if (objectType != typeof(byte[]))
        {
            throw _Unsupported(objectType);
        }

        return _ReadAllBytes(data);
    }

    /// <inheritdoc />
    public void Serialize(object? value, Stream output)
    {
        Argument.IsNotNull(value);

        if (value is not byte[] bytes)
        {
            throw _Unsupported(value.GetType());
        }

        output.Write(bytes);
    }

    /// <inheritdoc />
    public void Serialize<T>(T value, IBufferWriter<byte> output)
    {
        Argument.IsNotNull(value);

        if (value is not byte[] bytes)
        {
            throw _Unsupported(typeof(T));
        }

        // Identity codec: write the payload straight into the caller's buffer writer (for example a Redis frame
        // buffer or a network pipe) with no intermediate stream or array.
        output.Write(bytes);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(ReadOnlySequence<byte> data)
    {
        if (typeof(T) != typeof(byte[]))
        {
            throw _Unsupported(typeof(T));
        }

        // The contract returns a byte[], so the sequence must be copied once here; the zero-copy read path is
        // IBufferCache.TryGetToAsync, which writes the payload into a caller buffer without this materialization.
        return (T?)(object)data.ToArray();
    }

    private static byte[] _ReadAllBytes(Stream data)
    {
        // Fast path: a publicly-visible MemoryStream exposes its buffer, so copy the segment once instead of
        // the growth-doubling MemoryStream + trailing ToArray (two full copies).
        if (data is MemoryStream memory && memory.TryGetBuffer(out var buffer))
        {
            return buffer.AsSpan().ToArray();
        }

        // Seekable but not buffer-visible (e.g. a MemoryStream over a fixed array): pre-size and read once.
        if (data.CanSeek)
        {
            var remaining = data.Length - data.Position;

            if (remaining == 0)
            {
                return [];
            }

            var bytes = new byte[remaining];
            data.ReadExactly(bytes);

            return bytes;
        }

        using var output = new MemoryStream();
        data.CopyTo(output);

        return output.ToArray();
    }

    private static NotSupportedException _Unsupported(Type type) =>
        new($"RawBytesSerializer supports only byte[] values; '{type}' is not supported.");
}
