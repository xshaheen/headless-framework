// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;

namespace Framework.Serializer;

public static class SerializerExtensions
{
    public static T? Deserialize<T>(this ISerializer serializer, Stream data)
    {
        return serializer.Deserialize<T>(data);
    }

    public static T? Deserialize<T>(this ISerializer serializer, byte[] data)
    {
        return serializer.Deserialize<T>(new MemoryStream(data));
    }

    public static T? Deserialize<T>(this ISerializer serializer, string? data)
    {
        var bytes =
            data is null ? []
            : serializer is ITextSerializer ? Encoding.UTF8.GetBytes(data)
            : Convert.FromBase64String(data);

        return serializer.Deserialize<T>(new MemoryStream(bytes));
    }

    public static string? SerializeToString<T>(this ISerializer serializer, T? value)
    {
        if (value is null)
        {
            return null;
        }

        var bytes = serializer.SerializeToBytes(value);

        if (bytes is null)
        {
            return null;
        }

        return serializer is ITextSerializer ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes);
    }

    public static byte[]? SerializeToBytes<T>(this ISerializer serializer, T? value)
    {
        if (value is null)
        {
            return null;
        }

        var stream = new MemoryStream();
        serializer.Serialize(value, stream);

        return stream.ToArray();
    }
}
