// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;

namespace Framework.Tus.Models;

public class TusAzureFile
{
    public string FileId { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public long? UploadLength { get; set; }
    public long UploadOffset { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTimeOffset? ExpirationDate { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public bool IsComplete => UploadLength.HasValue && UploadOffset >= UploadLength.Value;
}
