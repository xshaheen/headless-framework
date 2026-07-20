// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Compression;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extension methods for compressing, decompressing, and comparing byte arrays.</summary>
[PublicAPI]
public static class HeadlessByteExtensions
{
    /// <summary>Compresses the given bytes using the Brotli algorithm.</summary>
    /// <param name="bytes">The bytes to compress.</param>
    /// <returns>The Brotli-compressed bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is <see langword="null"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static byte[] Compress(this byte[] bytes)
    {
        using var output = new MemoryStream();

        // Dispose the BrotliStream before reading the buffer: it flushes its trailing
        // compressed bytes only on dispose, so calling ToArray() while it is still open
        // yields truncated output. MemoryStream.ToArray() remains valid after disposal.
        using (var stream = new BrotliStream(output, CompressionMode.Compress))
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>Decompresses the given Brotli-compressed bytes.</summary>
    /// <param name="bytes">The Brotli-compressed bytes to decompress.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="bytes"/> is not valid Brotli-compressed data.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static byte[] Decompress(this byte[] bytes)
    {
        using var output = new MemoryStream();
        using var input = new MemoryStream(bytes);
        using var stream = new BrotliStream(input, CompressionMode.Decompress);
        stream.CopyTo(output);

        return output.ToArray();
    }

    /// <summary>
    /// Compares two byte[] arrays, element by element, and returns the index of the first element that differs.
    /// </summary>
    /// <param name="bytes1">The first byte[] to compare.</param>
    /// <param name="bytes2">The second byte[] to compare.</param>
    /// <returns>
    /// The index of the first differing element; if one array is a prefix of the other, the length of the shorter
    /// array (so identical arrays return their length).
    /// </returns>
    [SystemPure]
    [JetBrainsPure]
    public static int BytesDifference(this byte[] bytes1, byte[] bytes2)
    {
        var len1 = bytes1.Length;
        var len2 = bytes2.Length;
        var len = len1 < len2 ? len1 : len2;

        for (var i = 0; i < len; i++)
        {
            if (bytes1[i] != bytes2[i])
            {
                return i;
            }
        }

        return len;
    }
}
