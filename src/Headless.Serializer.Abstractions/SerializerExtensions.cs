// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

public static class SerializerExtensions
{
    extension(ISerializer serializer)
    {
        public T? Deserialize<T>(byte[] data)
        {
            return serializer.Deserialize<T>(new MemoryStream(data));
        }

        public T? Deserialize<T>(string? data)
        {
            var bytes =
                data is null ? []
                : serializer is ITextSerializer ? Encoding.UTF8.GetBytes(data)
                : Convert.FromBase64String(data);

            return serializer.Deserialize<T>(bytes);
        }

        public byte[]? SerializeToBytes<T>(T? value)
        {
            if (value is null)
            {
                return null;
            }

            var stream = new MemoryStream();
            serializer.Serialize(value, stream);

            return stream.ToArray();
        }

        public string? SerializeToString<T>(T? value)
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
    }
}
