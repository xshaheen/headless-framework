// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;

namespace Framework.Tus.Models;

public sealed class TusAzureFile
{
    public required string FileId { get; init; }

    public required string BlobName { get; init; }

    public long? UploadLength { get; init; }

    public long UploadOffset { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    public DateTimeOffset? ExpirationDate { get; init; }

    public DateTimeOffset CreatedDate { get; init; }

    public DateTimeOffset LastModified { get; init; }

    public bool IsComplete => UploadOffset >= UploadLength;
}
