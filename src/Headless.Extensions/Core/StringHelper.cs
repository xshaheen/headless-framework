// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>Helpers for working with strings and their byte representations.</summary>
[PublicAPI]
public static class StringHelper
{
    /// <summary>
    /// A shared UTF-8 <see cref="Encoding"/> instance that does not emit a byte order mark (BOM)
    /// when encoding.
    /// </summary>
    public static Encoding Utf8WithoutBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Decodes a byte array to a string, stripping a leading UTF-8 BOM if present.</summary>
    /// <param name="bytes">The byte array to decode, or <see langword="null"/> to return <see langword="null"/>.</param>
    /// <param name="encoding">The encoding used to decode the bytes. Defaults to <see cref="Encoding.UTF8"/> when <see langword="null"/>.</param>
    /// <returns>
    /// The decoded string with any leading UTF-8 BOM stripped, or <see langword="null"/> if
    /// <paramref name="bytes"/> is <see langword="null"/>.
    /// </returns>
    [return: NotNullIfNotNull(nameof(bytes))]
    public static string? ConvertFromBytesWithoutBom(byte[]? bytes, Encoding? encoding = null)
    {
        if (bytes is null)
        {
            return null;
        }

        encoding ??= Encoding.UTF8;

        var hasBom = bytes is [0xEF, 0xBB, 0xBF, ..];

        return hasBom ? encoding.GetString(bytes, 3, bytes.Length - 3) : encoding.GetString(bytes);
    }
}
