// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Headless.Checks;
using Headless.Core;
using Headless.IO;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

[PublicAPI]
public static class StreamExtensions
{
    #region Converter

    /// <summary>
    /// Exposes a <see cref="ReadOnlySequence{T}"/> of <see cref="byte"/> as a <see cref="Stream"/>.
    /// </summary>
    /// <param name="readOnlySequence">The sequence of bytes to expose as a stream.</param>
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
    public static Stream ReadSlice(this Stream stream, long length) => new NestedStream(stream, length);

    #endregion

    #region Get All Text

    [MustUseReturnValue]
    public static string GetAllText(this Stream stream, Encoding? encoding = null)
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();
        using var reader = new StreamReader(stream, encoding ?? StringHelper.Utf8WithoutBom, leaveOpen: true);
        var text = reader.ReadToEnd();

        return text;
    }

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

    [MustUseReturnValue]
    public static Task<string> GetAllTextAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        return stream.GetAllTextAsync(StringHelper.Utf8WithoutBom, cancellationToken);
    }

    #endregion

    #region Get All Bytes

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
        await writer.WriteAsync(text).ConfigureAwait(false);
    }

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

    /// <summary>At the end of the method, the position of both streams will be at the end.</summary>
    [MustUseReturnValue]
    public static MemoryStream CreateMemoryStream(this Stream stream)
    {
        Argument.IsNotNull(stream);

        stream.ResetPosition();
        var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        return memoryStream;
    }

    /// <summary>At the end of the method, the position of both streams will be at the end.</summary>
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

    public static void ResetPosition(this Stream stream, int position = 0)
    {
        if (stream.CanSeek)
        {
            stream.Position = position;
        }
    }

    #endregion

    #region Md5

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
        var data = await md5.ComputeHashAsync(stream, cancellationToken);

        var sb = new StringBuilder();

        foreach (var d in data)
        {
            sb.Append(d.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    #endregion
}
