// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;

namespace Framework.Tus.Models;

public sealed class AzureBlobInfo
{
    private AzureBlobInfo() { }

    public required string BlobName { get; init; }

    public required long Size { get; init; }

    public required IDictionary<string, string> Metadata { get; init; }

    public required DateTimeOffset LastModified { get; init; }

    public required string ETag { get; init; }

    public static AzureBlobInfo FromBlobProperties(BlobProperties properties, string blobName)
    {
        return new AzureBlobInfo
        {
            BlobName = blobName,
            Size = properties.ContentLength,
            Metadata = properties.Metadata,
            LastModified = properties.LastModified,
            ETag = properties.ETag.ToString(),
        };
    }
}
