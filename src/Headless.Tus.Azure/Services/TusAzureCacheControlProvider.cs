// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Headless.Constants;

namespace Headless.Tus.Services;

public interface ITusAzureBlobHttpHeadersProvider
{
    ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata);
}

public sealed class DefaultTusAzureBlobHttpHeadersProvider : ITusAzureBlobHttpHeadersProvider
{
    public ValueTask<BlobHttpHeaders> GetBlobHttpHeadersAsync(Dictionary<string, string> metadata)
    {
        var result = new BlobHttpHeaders { ContentType = ContentTypes.Applications.OctetStream };

        return ValueTask.FromResult(result);
    }
}
