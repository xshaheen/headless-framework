// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

/// <summary>
/// Provides the standard HTTP headers (content type, cache control, and so on) a finished upload's object carries.
/// </summary>
/// <remarks>
/// Implement this interface and register it in DI, or pass it to the <c>TusS3Store</c> constructor, to choose the
/// headers per upload. The <c>metadata</c> parameter holds the client's tus metadata as decoded key/value pairs: the
/// client's original keys (casing preserved) with UTF-8 decoded values. The headers are fixed when the object's
/// multipart upload starts or the object is written in one request, and S3 cannot change them afterwards without
/// copying the object.
/// </remarks>
[PublicAPI]
public interface ITusS3ObjectHeadersProvider
{
    /// <summary>Returns the headers the finished object of an upload carries.</summary>
    /// <param name="metadata">the client's tus metadata, decoded</param>
    /// <returns>the headers to store with the object</returns>
    ValueTask<TusS3ObjectHeaders> GetObjectHeadersAsync(Dictionary<string, string> metadata);
}

/// <summary>
/// The standard HTTP headers S3 stores with an object and returns when the object is downloaded. A
/// <see langword="null"/> value leaves that header unset.
/// </summary>
[PublicAPI]
public sealed record TusS3ObjectHeaders
{
    /// <summary>The <c>Content-Type</c> header; S3 answers <c>binary/octet-stream</c> or similar when unset.</summary>
    public string? ContentType { get; init; }

    /// <summary>The <c>Cache-Control</c> header.</summary>
    public string? CacheControl { get; init; }

    /// <summary>The <c>Content-Disposition</c> header, for example to suggest a download file name.</summary>
    public string? ContentDisposition { get; init; }

    /// <summary>The <c>Content-Encoding</c> header.</summary>
    public string? ContentEncoding { get; init; }

    /// <summary>The <c>Content-Language</c> header.</summary>
    public string? ContentLanguage { get; init; }
}

/// <summary>
/// Default <see cref="ITusS3ObjectHeadersProvider"/>: the content type follows <see cref="TusContentTypes.FromFileType"/>,
/// so it is the upload's <c>filetype</c> metadata when that is a well-formed media type a browser would not execute,
/// and <c>application/octet-stream</c> otherwise. No other header is set.
/// </summary>
[PublicAPI]
public sealed class DefaultTusS3ObjectHeadersProvider : ITusS3ObjectHeadersProvider
{
    /// <inheritdoc />
    public ValueTask<TusS3ObjectHeaders> GetObjectHeadersAsync(Dictionary<string, string> metadata)
    {
        return ValueTask.FromResult(new TusS3ObjectHeaders { ContentType = TusContentTypes.FromFileType(metadata) });
    }
}
