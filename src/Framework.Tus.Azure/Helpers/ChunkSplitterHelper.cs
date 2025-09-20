// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Framework.Tus.Models;
using Framework.Tus.Options;

namespace Framework.Tus.Helpers;

/// <summary>
/// Handles splitting large chunks into smaller ones that Azure can handle
/// </summary>
public static class ChunkSplitterHelper
{
    /// <summary>
    /// Splits a stream into chunks of the specified maximum size
    /// </summary>
    public static async IAsyncEnumerable<MemoryStream> SplitStreamAsync(
        Stream sourceStream,
        int maxChunkSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var buffer = new byte[maxChunkSize];

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, maxChunkSize), cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            var chunk = new MemoryStream();
            await chunk.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            chunk.Position = 0;

            yield return chunk;
        }
    }

    /// <summary>
    /// Calculates the optimal chunk size based on total data size and blob type
    /// </summary>
    public static int CalculateOptimalChunkSize(long totalSize, BlobType blobType, TusAzureStoreOptions config)
    {
        var maxChunkSize = BlobStrategyHelper.GetMaxChunkSize(blobType, config);
        var defaultChunkSize = BlobStrategyHelper.GetDefaultChunkSize(blobType, config);

        return totalSize switch
        {
            // (Less than 10MB) For small files, use smaller chunks to reduce memory usage
            < 10 * 1024 * 1024 => Math.Min(defaultChunkSize, (int)totalSize),
            // (Less than 100MB) For medium files, use default chunk size
            < 100 * 1024 * 1024 => defaultChunkSize,
            // (100MB and above) For large files, use larger chunks for better performance
            _ => maxChunkSize,
        };
    }
}
