// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.Blobs;

/// <summary>
/// Response headers a presigned download URL makes the backend send, passed to
/// <see cref="IPresignedUrlBlobStorage.GetPresignedDownloadUrlAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The values are part of the signed URL, so whoever holds the URL cannot change them. S3 honors both through the
/// <c>response-content-disposition</c> and <c>response-content-type</c> query parameters, Azure through the SAS
/// <c>rscd</c> and <c>rsct</c> fields, and the signed-URL endpoint in <c>Headless.Blobs.SignedUrlEndpoint</c> by
/// writing the headers itself. Cloudflare R2 receives the same S3 query parameters, but its published S3
/// compatibility table does not list them for <c>GetObject</c>, so confirm the response headers against R2.
/// </para>
/// <para>
/// The setters validate, so an instance never carries a value that could split or inject a response header.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record PresignedDownloadOptions
{
    /// <summary>
    /// The file name a browser saves the download as, or <see langword="null"/> to keep the backend's default. It is
    /// sent as an <c>attachment</c> <c>Content-Disposition</c> with an ASCII <c>filename</c> fallback and an RFC 5987
    /// <c>filename*</c> that carries the exact name, so non-ASCII names such as Arabic survive.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty, white space, or contains a control character.</exception>
    public string? FileName
    {
        get;
        init
        {
            if (value is not null)
            {
                Argument.IsNotNullOrWhiteSpace(value);
                Argument.IsFalse(
                    _HasControlCharacter(value),
                    "The file name must not contain control characters.",
                    nameof(FileName)
                );
            }

            field = value;
        }
    }

    /// <summary>
    /// The media type sent as the download's <c>Content-Type</c>, such as <c>application/pdf</c>, or
    /// <see langword="null"/> to keep the type stored with the blob.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a single media type or contains a control character.</exception>
    public string? ContentType
    {
        get;
        init
        {
            if (value is not null)
            {
                // The parser tolerates surrounding white space and folds, so control characters are refused first.
                Argument.IsTrue(
                    !_HasControlCharacter(value)
                        && MediaTypeHeaderValue.TryParse(value, out var mediaType)
                        && mediaType.MediaType is { } type
                        && !type.Contains('*', StringComparison.Ordinal),
                    $"'{value}' is not a single media type such as 'application/pdf'.",
                    nameof(ContentType)
                );
            }

            field = value;
        }
    }

    private static bool _HasControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }
}
