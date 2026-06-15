// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// Identity serializer for cache instances whose values are already raw <see cref="byte"/> arrays.
/// </summary>
/// <remarks>
/// This serializer intentionally supports only <see cref="byte"/> arrays. It is intended for named caches that
/// are byte-oriented by construction, such as BCL <c>IDistributedCache</c> adapters.
/// </remarks>
[PublicAPI]
public sealed class RawBytesSerializer : IBinarySerializer
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
        ArgumentNullException.ThrowIfNull(value);

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
        ArgumentNullException.ThrowIfNull(value);

        if (value is not byte[] bytes)
        {
            throw _Unsupported(value.GetType());
        }

        output.Write(bytes);
    }

    private static byte[] _ReadAllBytes(Stream data)
    {
        using var output = new MemoryStream();
        data.CopyTo(output);
        return output.ToArray();
    }

    private static NotSupportedException _Unsupported(Type type) =>
        new($"RawBytesSerializer supports only byte[] values; '{type}' is not supported.");
}
