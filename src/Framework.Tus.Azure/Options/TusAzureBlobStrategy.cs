// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Tus.Options;

/// <summary>Strategy for determining which Azure Blob type to use for uploads.</summary>
public enum AzureBlobStrategy
{
    /// <summary>Automatically decide based on file size and characteristics</summary>
    Auto = 0,

    /// <summary>Use Block Blobs exclusively for all uploads (recommended for most scenarios)</summary>
    BlockBlobOnly = 1,

    /// <summary>Use Append Blobs first, then Block Blobs for larger files.</summary>
    AppendBlobFirst = 2,
}
