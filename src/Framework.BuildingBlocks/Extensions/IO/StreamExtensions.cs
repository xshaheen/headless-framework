// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

/// <summary>Provides a set of extension methods for operations on <see cref="Stream"/>.</summary>
[PublicAPI]
public static class StreamExtensions
{
    [MustUseReturnValue]
    public static string GetAllText(this Stream stream, Encoding? encoding = null)
    {
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8);

        return reader.ReadToEnd();
    }

    [MustUseReturnValue]
    public static async Task<string> GetAllTextAsync(
        this Stream stream,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default
    )
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    [MustUseReturnValue]
    public static async Task<string> GetAllTextAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        return await stream.GetAllTextAsync(Encoding.UTF8, cancellationToken);
    }

    [MustUseReturnValue]
    public static byte[] GetAllBytes(this Stream stream)
    {
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
        if (stream is MemoryStream s)
        {
            return s.ToArray();
        }

        await using var ms = await stream.CreateMemoryStreamAsync(cancellationToken);

        return ms.ToArray();
    }

    [MustUseReturnValue]
    public static async Task<MemoryStream> CreateMemoryStreamAsync(
        this Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        memoryStream.Position = 0;

        return memoryStream;
    }

    [MustUseReturnValue]
    public static MemoryStream CreateMemoryStream(this Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        memoryStream.Position = 0;

        return memoryStream;
    }

    public static Task CopyToAsync(this Stream source, Stream destination, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
        }

        return source.CopyToAsync(
            destination,
            81920, // this is already the default value, but needed to set to be able to pass the cancellationToken
            cancellationToken
        );
    }
}
