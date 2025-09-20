// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Tus.Models;

public class TusAzureBlobInfo
{
    public string FileId { get; set; } = string.Empty;

    public BlobType Type { get; set; }

    public long? UploadLength { get; set; }

    public long UploadOffset { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();

    public DateTimeOffset? ExpirationDate { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public bool IsComplete => UploadLength.HasValue && UploadOffset >= UploadLength.Value;

    public int CommittedBlockCount { get; set; } = 0;
}

public enum BlobType
{
    AppendBlob,
    BlockBlob,
}
