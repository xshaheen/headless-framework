// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Headless.Checks;
using Headless.Http;

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
/// Default <see cref="ITusS3ObjectHeadersProvider"/>: the content type is the upload's <c>filetype</c> metadata when it
/// is a valid media type, and <c>application/octet-stream</c> otherwise. No other header is set.
/// </summary>
[PublicAPI]
public sealed class DefaultTusS3ObjectHeadersProvider : ITusS3ObjectHeadersProvider
{
    /// <summary>The metadata key tus clients (tus-js-client, Uppy) send the file's media type under.</summary>
    public const string FileTypeMetadataKey = "filetype";

    /// <inheritdoc />
    public ValueTask<TusS3ObjectHeaders> GetObjectHeadersAsync(Dictionary<string, string> metadata)
    {
        Argument.IsNotNull(metadata);

        var contentType =
            metadata.TryGetValue(FileTypeMetadataKey, out var fileType) && _IsMediaType(fileType)
                ? fileType.Trim()
                : ContentTypes.Applications.OctetStream;

        return ValueTask.FromResult(new TusS3ObjectHeaders { ContentType = contentType });
    }

    // The value is client input that becomes a stored response header, so anything that does not parse as a media type
    // (no subtype, stray characters, line breaks) falls back instead of reaching storage.
    private static bool _IsMediaType(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && MediaTypeHeaderValue.TryParse(value, out var parsed)
            && parsed.MediaType?.Contains('/', StringComparison.Ordinal) == true;
    }
}
