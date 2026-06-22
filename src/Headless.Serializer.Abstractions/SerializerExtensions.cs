// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// Convenience overloads for <see cref="ISerializer"/> that operate on <c>byte[]</c> and <c>string</c>
/// instead of <see cref="Stream"/>.
/// </summary>
public static class SerializerExtensions
{
    extension(ISerializer serializer)
    {
        /// <summary>Deserializes <paramref name="data"/> into an instance of <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="data">The raw serialized bytes.</param>
        /// <returns>The deserialized value, or <see langword="null"/> for a null/absent payload.</returns>
        public T? Deserialize<T>(byte[] data)
        {
            return serializer.Deserialize<T>(new MemoryStream(data));
        }

        /// <summary>
        /// Deserializes a string representation into an instance of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="data">
        /// The serialized payload as a string. For <see cref="ITextSerializer"/> implementations (e.g. JSON) the
        /// string is decoded as UTF-8 bytes. For <see cref="IBinarySerializer"/> implementations (e.g. MessagePack)
        /// the string is treated as Base64. Pass <see langword="null"/> to get the default value of
        /// <typeparamref name="T"/>.
        /// </param>
        /// <returns>The deserialized value, or <see langword="null"/> when <paramref name="data"/> is <see langword="null"/>.</returns>
        public T? Deserialize<T>(string? data)
        {
            var bytes =
                data is null ? []
                : serializer is ITextSerializer ? Encoding.UTF8.GetBytes(data)
                : Convert.FromBase64String(data);

            return serializer.Deserialize<T>(bytes);
        }

        /// <summary>Serializes <paramref name="value"/> and returns the result as a byte array.</summary>
        /// <typeparam name="T">The static type of the value being serialized.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>The serialized bytes, or <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.</returns>
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

        /// <summary>
        /// Serializes <paramref name="value"/> and returns the result as a string.
        /// </summary>
        /// <typeparam name="T">The static type of the value being serialized.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>
        /// For <see cref="ITextSerializer"/> implementations (e.g. JSON), the raw UTF-8 string. For
        /// <see cref="IBinarySerializer"/> implementations (e.g. MessagePack), a Base64-encoded string.
        /// Returns <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.
        /// </returns>
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
