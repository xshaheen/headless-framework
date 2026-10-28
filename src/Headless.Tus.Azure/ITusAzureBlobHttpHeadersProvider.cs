// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;

namespace Headless.Tus;

/// <summary>
/// Provides Azure Blob HTTP headers (content type, cache control, etc.) for newly created upload blobs.
/// </summary>
/// <remarks>
/// Implement this interface and register it in DI to customize the <c>BlobHttpHeaders</c> set when
/// <c>TusAzureStore</c> creates a new blob. The <c>metadata</c> parameter contains the
/// user-supplied TUS metadata as decoded key/value pairs — the client's original keys (casing
/// preserved) with UTF-8 decoded values; the store's internal <c>tus_*</c> tracking keys are
/// never included — allowing header derivation from file name or content-type hints.
/// </remarks>
[PublicAPI]
public interface ITusAzureBlobHttpHeadersProvider
{
    /// <summary>
    /// Returns the <c>BlobHttpHeaders</c> to apply when creating an upload blob.
    /// </summary>
    /// <param name="metadata">user-supplied TUS metadata, with internal system keys excluded</param>
    /// <returns>the headers to set on the newly created Azure Blob</returns>
    ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata);
}

/// <summary>
/// Default <see cref="ITusAzureBlobHttpHeadersProvider"/>: the blob content type follows
/// <see cref="TusContentTypes.FromFileType"/>, so it is the upload's <c>filetype</c> metadata when that is a
/// well-formed media type a browser would not execute, and <c>application/octet-stream</c> otherwise. No other header
/// is set.
/// </summary>
[PublicAPI]
public sealed class DefaultTusAzureBlobHttpHeadersProvider : ITusAzureBlobHttpHeadersProvider
{
    /// <inheritdoc />
    public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
    {
        return ValueTask.FromResult(new BlobHttpHeaders { ContentType = TusContentTypes.FromFileType(metadata) });
    }
}
