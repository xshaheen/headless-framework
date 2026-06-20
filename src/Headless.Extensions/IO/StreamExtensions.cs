// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using Headless.Checks;
using Headless.Core;
using Headless.IO;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.IO;

/// <summary>Extension methods for reading from, writing to, and converting <see cref="Stream"/> instances.</summary>
[PublicAPI]
public static class StreamExtensions
{
    #region Converter

    /// <summary>
    /// Exposes a <see cref="ReadOnlySequence{T}"/> of <see cref="byte"/> as a <see cref="Stream"/>.
    /// </summary>
    /// <param name="readOnlySequence">The sequence of bytes to expose as a stream.</param>
    /// <param name="disposeAction">An optional action invoked when the returned stream is disposed.</param>
    /// <param name="disposeActionArg">An optional argument passed to <paramref name="disposeAction"/>.</param>
    /// <returns>The readable stream.</returns>
    public static Stream ToStream(
        this ReadOnlySequence<byte> readOnlySequence,
        Action<object?>? disposeAction = null,
        object? disposeActionArg = null
    )
    {
        return new ReadOnlySequenceStream(readOnlySequence, disposeAction, disposeActionArg);
    }

    #endregion

    #region Slice

    /// <summary>
    /// Creates a <see cref="Stream"/> that can read no more than a given number of bytes from an underlying stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="length">The number of bytes to read from the parent stream.</param>
    /// <returns>A stream that ends after <paramref name="length"/> bytes are read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> does not support reading.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is negative.</exception>
    public static Stream ReadSlice(this Stream stream, long length) => new NestedStream(stream, length);

    #endregion

    #region Get All Text

    /// <summary>
    /// Reads the entire <paramref name="stream"/> as text. The stream is rewound to the start first (when seekable)
    /// and left open afterwards.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="encoding">The encoding to decode with. Defaults to UTF-8 without a byte-order mark.</param>
    /// <returns>The full content of the stream as a string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the stream.</exception>
    [MustUseReturnValue]
    public static string GetAllText(this Stream stream, Encoding? encoding = null)
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();
        using var reader = new StreamReader(stream, encoding ?? StringHelper.Utf8WithoutBom, leaveOpen: true);
        var text = reader.ReadToEnd();

        return text;
    }

    /// <summary>
    /// Asynchronously reads the entire <paramref name="stream"/> as text. The stream is rewound to the start first
    /// (when seekable) and left open afterwards.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="encoding">The encoding to decode with.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>A task whose result is the full content of the stream as a string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> or <paramref name="encoding"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the stream.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    [MustUseReturnValue]
    public static async Task<string> GetAllTextAsync(
        this Stream stream,
        Encoding encoding,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);
        Argument.IsNotNull(encoding);

        stream.ResetPosition();
        using var reader = new StreamReader(stream, encoding, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);

        return text;
    }

    /// <summary>
    /// Asynchronously reads the entire <paramref name="stream"/> as text using UTF-8 without a byte-order mark. The
    /// stream is rewound to the start first (when seekable) and left open afterwards.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>A task whose result is the full content of the stream as a string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the stream.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    [MustUseReturnValue]
    public static Task<string> GetAllTextAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        return stream.GetAllTextAsync(StringHelper.Utf8WithoutBom, cancellationToken);
    }

    #endregion

    #region Get All Bytes

    /// <summary>
    /// Reads the entire <paramref name="stream"/> into a byte array. The stream is rewound to the start first (when seekable).
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>A byte array containing the full content of the stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the stream.</exception>
    [MustUseReturnValue]
    public static byte[] GetAllBytes(this Stream stream)
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();

        if (stream is MemoryStream s)
        {
            return s.ToArray();
        }

        using var ms = stream.CreateMemoryStream();

        return ms.ToArray();
    }

    /// <summary>
    /// Asynchronously reads the entire <paramref name="stream"/> into a byte array. The stream is rewound to the start
    /// first (when seekable).
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>A task whose result is a byte array containing the full content of the stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the stream.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    [MustUseReturnValue]
    public static async Task<byte[]> GetAllBytesAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();

        if (stream is MemoryStream s)
        {
            return s.ToArray();
        }

        await using var ms = await stream.CreateMemoryStreamAsync(cancellationToken);

        return ms.ToArray();
    }

    #endregion

    #region Write Text

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="stream"/> using the given encoding, leaving the stream open.
    /// Does nothing when <paramref name="text"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="text">The text to write; when <see langword="null"/> the method is a no-op.</param>
    /// <param name="encoding">The encoding to use. Defaults to UTF-8 without a byte-order mark.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while writing to the stream.</exception>
    public static void WriteText(this Stream stream, string? text, Encoding? encoding = null)
    {
        Argument.IsNotNull(stream);

        if (text is null)
        {
            return;
        }

        using var writer = new StreamWriter(stream, encoding ?? StringHelper.Utf8WithoutBom, leaveOpen: true);

        writer.Write(text);
    }

    /// <summary>
    /// Asynchronously writes <paramref name="text"/> to <paramref name="stream"/> using the given encoding, leaving the
    /// stream open. Does nothing when <paramref name="text"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="text">The text to write; when <see langword="null"/> the method is a no-op.</param>
    /// <param name="encoding">The encoding to use.</param>
    /// <param name="cancellationToken">A token to observe while writing.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> or <paramref name="encoding"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while writing to the stream.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static async ValueTask WriteTextAsync(
        this Stream stream,
        string? text,
        Encoding encoding,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);
        Argument.IsNotNull(encoding);

        if (text is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes <paramref name="text"/> to <paramref name="stream"/> using UTF-8 without a byte-order
    /// mark, leaving the stream open. Does nothing when <paramref name="text"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="text">The text to write; when <see langword="null"/> the method is a no-op.</param>
    /// <param name="cancellationToken">A token to observe while writing.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while writing to the stream.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static ValueTask WriteTextAsync(
        this Stream stream,
        string? text,
        CancellationToken cancellationToken = default
    )
    {
        return stream.WriteTextAsync(text, StringHelper.Utf8WithoutBom, cancellationToken);
    }

    #endregion

    #region Create Memory Stream

    /// <summary>
    /// Copies the entire content of <paramref name="stream"/> into a new <see cref="MemoryStream"/>. The source is
    /// rewound to the start first (when seekable). At the end of the method, the position of both streams will be at the end.
    /// </summary>
    /// <param name="stream">The source stream to copy from.</param>
    /// <returns>A new <see cref="MemoryStream"/> containing the copied content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while copying.</exception>
    [MustUseReturnValue]
    public static MemoryStream CreateMemoryStream(this Stream stream)
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();
        var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        return memoryStream;
    }

    /// <summary>
    /// Asynchronously copies the entire content of <paramref name="stream"/> into a new <see cref="MemoryStream"/>. The
    /// source is rewound to the start first (when seekable). At the end of the method, the position of both streams will be at the end.
    /// </summary>
    /// <param name="stream">The source stream to copy from.</param>
    /// <param name="cancellationToken">A token to observe while copying.</param>
    /// <returns>A task whose result is a new <see cref="MemoryStream"/> containing the copied content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while copying.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    [MustUseReturnValue]
    public static async Task<MemoryStream> CreateMemoryStreamAsync(
        this Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        return memoryStream;
    }

    #endregion

    #region Copy To

    /// <summary>
    /// Asynchronously copies the content of <paramref name="source"/> to <paramref name="destination"/> while honoring
    /// <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="source">The source stream to copy from.</param>
    /// <param name="destination">The destination stream to copy to.</param>
    /// <param name="cancellationToken">A token to observe while copying.</param>
    /// <returns>A task that represents the asynchronous copy operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when an I/O error occurs while copying.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static Task CopyToAsync(this Stream source, Stream destination, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(source);

        return source.CopyToAsync(
            destination,
            81920, // this is already the default value, but needed to set to be able to pass the cancellationToken
            cancellationToken
        );
    }

    #endregion

    #region Reset Position

    /// <summary>
    /// Sets <paramref name="stream"/>'s position to <paramref name="position"/> when the stream supports seeking;
    /// otherwise does nothing.
    /// </summary>
    /// <param name="stream">The stream to reposition.</param>
    /// <param name="position">The position to seek to. Defaults to the start of the stream (<c>0</c>).</param>
    public static void ResetPosition(this Stream stream, int position = 0)
    {
        if (stream.CanSeek)
        {
            stream.Position = position;
        }
    }

    #endregion

    #region Md5

    /// <summary>Computes the MD5 hash of <paramref name="stream"/> from its current position and returns it as an uppercase hexadecimal string.</summary>
    /// <param name="stream">The stream to hash.</param>
    /// <param name="cancellationToken">A token to observe while hashing.</param>
    /// <returns>A task whose result is the MD5 hash encoded as an uppercase hexadecimal string.</returns>
    /// <exception cref="IOException">Thrown when an I/O error occurs while reading the stream.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 is used for file integrity check."
    )]
    public static async Task<string> CalculateMd5Async(
        this Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        using var md5 = MD5.Create();
        var data = await md5.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(data);
    }

    #endregion
}
