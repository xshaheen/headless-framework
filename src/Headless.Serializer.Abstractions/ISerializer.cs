// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Runtime.InteropServices;

namespace Headless.Serializer;

public interface ISerializer
{
    T? Deserialize<T>(Stream data);

    void Serialize<T>(T value, Stream output);

    object? Deserialize(Stream data, Type objectType);

    void Serialize(object? value, Stream output);
}

public interface IBinarySerializer : ISerializer
{
    /// <summary>
    /// Serializes <paramref name="value"/> directly into <paramref name="output"/>, avoiding an intermediate
    /// stream when the implementation supports it. The default bridges through
    /// <see cref="ISerializer.Serialize{T}(T, Stream)"/>; perf-critical serializers override for a zero-copy path.
    /// </summary>
    void Serialize<T>(T value, IBufferWriter<byte> output)
    {
        using var stream = new MemoryStream();
        Serialize(value, stream);

        if (stream.TryGetBuffer(out var buffer))
        {
            output.Write(buffer.AsSpan());
        }
        else
        {
            output.Write(stream.ToArray());
        }
    }

    /// <summary>
    /// Deserializes a value from <paramref name="data"/>, avoiding an intermediate stream when the implementation
    /// supports it. The default bridges through <see cref="ISerializer.Deserialize{T}(Stream)"/>; perf-critical
    /// serializers override for a zero-copy path.
    /// </summary>
    T? Deserialize<T>(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment && MemoryMarshal.TryGetArray(data.First, out var segment) && segment.Array is not null)
        {
            using var single = new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false);

            return Deserialize<T>(single);
        }

        using var stream = new MemoryStream();

        foreach (var memory in data)
        {
            stream.Write(memory.Span);
        }

        stream.Position = 0;

        return Deserialize<T>(stream);
    }
}

public interface ITextSerializer : ISerializer;

public interface IJsonSerializer : ITextSerializer;
