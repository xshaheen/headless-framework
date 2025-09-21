// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Tus.Models;

public class TusAzureBlobInfo
{
    public required string FileId { get; set; }

    public int CommittedBlockCount { get; set; }
}
