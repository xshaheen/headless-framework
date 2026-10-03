// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Headless.Constants;

namespace Headless.Tus;

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
