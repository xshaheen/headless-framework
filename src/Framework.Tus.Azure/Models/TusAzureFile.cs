// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;

namespace Framework.Tus.Models;

internal sealed class TusAzureFile
{
    private TusAzureFile() { }

    public required string FileId { get; init; }

    public required string BlobName { get; init; }

    public long CurrentContentLength { get; init; }

    public required string ETag { get; init; }

    public required TusAzureMetadata Metadata { get; init; }

    public static TusAzureFile FromBlobProperties(string fileId, string blobName, BlobProperties properties)
    {
        return new TusAzureFile
        {
            FileId = fileId,
            BlobName = blobName,
            CurrentContentLength = properties.ContentLength,
            Metadata = TusAzureMetadata.FromAzure(properties.Metadata),
            ETag = properties.ETag.ToString(),
        };
    }
}
