// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Tus.Models;
using Framework.Tus.Options;

namespace Framework.Tus.Helpers;

public static class BlobStrategyHelper
{
    /// <summary>
    /// Determines the appropriate blob type based on configuration and file characteristics
    /// </summary>
    public static BlobType DetermineBlobType(
        TusAzureStoreOptions config,
        long? uploadLength,
        Dictionary<string, string> metadata
    )
    {
        switch (config.BlobStrategy)
        {
            case AzureBlobStrategy.BlockBlobOnly:
                return BlobType.BlockBlob;

            case AzureBlobStrategy.AppendBlobFirst:
                // Use Append Blob for smaller files, Block Blob for larger ones
                if (uploadLength.HasValue && uploadLength.Value > config.AppendBlobSizeLimit)
                {
                    return BlobType.BlockBlob;
                }
                return BlobType.AppendBlob;

            case AzureBlobStrategy.Auto:
                // Intelligent decision based on file characteristics
                return _DetermineOptimalBlobType(config, uploadLength, metadata);

            default:
                return BlobType.BlockBlob;
        }
    }

    private static BlobType _DetermineOptimalBlobType(
        TusAzureStoreOptions config,
        long? uploadLength,
        Dictionary<string, string> metadata
    )
    {
        // If size is unknown or deferred, start with Append Blob
        if (!uploadLength.HasValue || uploadLength.Value == -1)
        {
            return BlobType.AppendBlob;
        }

        // For very large files, use Block Blob
        if (uploadLength.Value > config.AppendBlobSizeLimit)
        {
            return BlobType.BlockBlob;
        }

        // Check content type for optimization hints
        if (metadata.TryGetValue("content-type", out var contentType))
        {
            // Log files, text files, and similar sequential data work well with Append Blobs
            var appendOptimizedTypes = new[] { "text/", "application/json", "application/xml", "application/log" };
            if (appendOptimizedTypes.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            {
                return BlobType.AppendBlob;
            }

            // Binary files, media files work better with Block Blobs
            var blockOptimizedTypes = new[] { "video/", "image/", "application/octet-stream", "application/zip" };
            if (blockOptimizedTypes.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            {
                return BlobType.BlockBlob;
            }
        }

        // Default to Append Blob for smaller files
        return BlobType.AppendBlob;
    }

    /// <summary>
    /// Gets the maximum chunk size for the specified blob type
    /// </summary>
    public static int GetMaxChunkSize(BlobType blobType, TusAzureStoreOptions config)
    {
        return blobType switch
        {
            BlobType.AppendBlob => config.AppendBlobMaxChunkSize,
            BlobType.BlockBlob => config.BlockBlobMaxChunkSize,
            _ => config.BlockBlobDefaultChunkSize,
        };
    }

    /// <summary>
    /// Gets the default chunk size for the specified blob type
    /// </summary>
    public static int GetDefaultChunkSize(BlobType blobType, TusAzureStoreOptions config)
    {
        return blobType switch
        {
            BlobType.AppendBlob => config.AppendBlobMaxChunkSize, // Always use max for append
            BlobType.BlockBlob => config.BlockBlobDefaultChunkSize,
            _ => config.BlockBlobDefaultChunkSize,
        };
    }
}
