// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;

namespace Headless.Tus.Models;

internal sealed class TusAzureFile
{
    private TusAzureFile() { }

    public required string FileId { get; init; }

    public required string BlobName { get; init; }

    public long CurrentContentLength { get; init; }

    public required string ETag { get; init; }

    public required TusAzureMetadata Metadata { get; init; }

    /// <summary>
    /// The blob's current HTTP headers (content type, cache control, …), captured so that every
    /// <c>CommitBlockList</c> re-supplies them: Azure's Put Block List <em>clears</em> any
    /// <c>x-ms-blob-*</c> property omitted from the request, which would silently wipe headers a
    /// custom <c>ITusAzureBlobHttpHeadersProvider</c> set at creation on the first PATCH.
    /// <c>ContentHash</c> is deliberately not carried over — the content changes with each commit,
    /// so echoing the stored MD5 would persist a stale digest.
    /// </summary>
    public required BlobHttpHeaders HttpHeaders { get; init; }

    public static TusAzureFile FromBlobProperties(string fileId, string blobName, BlobProperties properties)
    {
        return new TusAzureFile
        {
            FileId = fileId,
            BlobName = blobName,
            CurrentContentLength = properties.ContentLength,
            Metadata = TusAzureMetadata.FromAzure(properties.Metadata),
            ETag = properties.ETag.ToString(),
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = properties.ContentType,
                CacheControl = properties.CacheControl,
                ContentEncoding = properties.ContentEncoding,
                ContentLanguage = properties.ContentLanguage,
                ContentDisposition = properties.ContentDisposition,
            },
        };
    }
}
