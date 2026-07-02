// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Headless.Constants;

namespace Headless.Tus.Services;

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
/// Default implementation of <c>ITusAzureBlobHttpHeadersProvider</c> that sets
/// <c>application/octet-stream</c> as the blob content type for every upload.
/// </summary>
[PublicAPI]
public sealed class DefaultTusAzureBlobHttpHeadersProvider : ITusAzureBlobHttpHeadersProvider
{
    /// <summary>
    /// Returns <c>BlobHttpHeaders</c> with <c>ContentType</c> set to
    /// <c>application/octet-stream</c>.
    /// </summary>
    /// <param name="metadata">user-supplied TUS metadata (unused by this default implementation)</param>
    /// <returns>blob HTTP headers with a generic binary content type</returns>
    public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
    {
        var result = new BlobHttpHeaders { ContentType = ContentTypes.Applications.OctetStream };

        return ValueTask.FromResult(result);
    }
}
