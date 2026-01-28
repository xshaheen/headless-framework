// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using tusdotnet.Extensions.Store;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore
{
    public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(fileId);
        Argument.IsNotNull(stream);

        _logger.LogTrace("Appending data using the Stream for file '{FileId}'", fileId);

        var blobClient = _GetBlobClient(fileId);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        var azureFile =
            await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken)
            ?? throw new InvalidOperationException($"File {fileId} does not exist");

        var committedBlocks = await _GetCommittedBlocksAsync(blockBlobClient, cancellationToken);
        using var hasher = await _GetHasher(stream, cancellationToken);

        // Stage blocks (with or without chunking)
        var (chunkBlockIds, bytesWritten) = await _StageAsync(
            blockBlobClient,
            stream,
            nextBlockNumber: committedBlocks.Count,
            azureFile.Metadata.UploadLength,
            hasher: hasher,
            cancellationToken: cancellationToken
        );

        // ATOMIC: Commit blocks + update metadata in single operation for non-checksum uploads
        if (hasher == null)
        {
            List<string> allBlockIds = [.. committedBlocks.Select(b => b.Name), .. chunkBlockIds];
            var options = new CommitBlockListOptions { Metadata = azureFile.Metadata.ToAzure() };
            await blockBlobClient.CommitBlockListAsync(allBlockIds, options, cancellationToken);

            return bytesWritten;
        }

        // Store the block IDs for this chunk - these are the blocks that will need to be committed or rolled back

        azureFile.Metadata.LastChunkBlocks = chunkBlockIds.ToArray();
        azureFile.Metadata.LastChunkChecksum = Convert.ToBase64String(hasher.Hash ?? []);
        await _UpdateMetadataAsync(blobClient, azureFile, cancellationToken);

        _logger.LogDebug(
            "Stored chunk metadata for file '{FileId}': {BlockCount} blocks staged for checksum verification",
            fileId,
            chunkBlockIds.Count
        );

        return bytesWritten;
    }

    /// <summary>
    /// Stages blocks to the block blob, optionally splitting into chunks for large streams.
    /// </summary>
    private async Task<(List<string> BlockIds, long BytesWritten)> _StageAsync(
        BlockBlobClient blockBlobClient,
        Stream stream,
        int nextBlockNumber,
        long? fileUploadLength,
        HashAlgorithm? hasher,
        CancellationToken cancellationToken
    )
    {
        if (!_options.EnableChunkSplitting)
        {
            var blockId = _GenerateBlockId(nextBlockNumber);

            if (hasher is null) // No checksum, upload directly
            {
                await blockBlobClient.StageBlockAsync(blockId, stream, cancellationToken: cancellationToken);
                return ([blockId], stream.Length);
            }

            // Read entire stream into MemoryStream for hashing and upload
            // Pre-allocate capacity if stream length is known to avoid resizing
            var capacity = stream is { CanSeek: true, Length: > 0 } ? (int)stream.Length : 0;
            await using var memoryStream = new MemoryStream(capacity);
            await stream.CopyToAsync(memoryStream, cancellationToken).AnyContext();

            memoryStream.Position = 0; // Reset position for hashing
            hasher.TransformFinalBlock(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);

            memoryStream.Position = 0; // Reuse MemoryStream for upload
            await blockBlobClient
                .StageBlockAsync(blockId, memoryStream, cancellationToken: cancellationToken)
                .AnyContext();
            return ([blockId], memoryStream.Length);
        }

        var maxChunkSize = _CalculateOptimalChunkSize(fileUploadLength);

        // Pre-allocate list capacity to avoid resizing during iteration
        var estimatedChunkCount = fileUploadLength.HasValue
            ? (int)Math.Ceiling((double)fileUploadLength.Value / maxChunkSize)
            : 16; // Default capacity if length unknown

        var chunkBlockIds = new List<string>(estimatedChunkCount);
        var bytesWritten = 0L;

        await foreach (var chunk in _SplitStreamAsync(stream, maxChunkSize, cancellationToken).AnyContext())
        {
            var blockId = _GenerateBlockId(nextBlockNumber++);

            // Calculate hash for this chunk if needed
            // TransformBlock uses the buffer synchronously - safe with shared buffer approach
            hasher?.TransformBlock(chunk.Array!, chunk.Offset, chunk.Count, outputBuffer: null, 0);

            // Upload the chunk as a block
            // MemoryStream wrapper is necessary (Azure SDK requires Stream) but doesn't copy data
            // Azure SDK reads/buffers the stream synchronously - safe with shared buffer approach
            await using var chunkStream = new MemoryStream(chunk.Array!, chunk.Offset, chunk.Count, writable: false);
            await blockBlobClient
                .StageBlockAsync(blockId, chunkStream, cancellationToken: cancellationToken)
                .AnyContext();

            chunkBlockIds.Add(blockId);
            bytesWritten += chunk.Count;
        }

        // Finalize hash if needed (TransformFinalBlock with empty data)
        hasher?.TransformFinalBlock([], 0, 0);

        return (chunkBlockIds, bytesWritten);
    }

    /// <summary>Splits a stream into chunks of the specified maximum size.</summary>
    /// <param name="sourceStream">The source stream to read and split into chunks.</param>
    /// <param name="chunkSize">Maximum size of each chunk in bytes.</param>
    /// <param name="cancellationToken">Token to cancel the async enumeration.</param>
    /// <returns>Async enumerable of ArraySegments referencing portions of a pooled buffer.</returns>
    /// <remarks>
    /// <para>
    /// Performance optimizations:
    /// - Uses ArrayPool to eliminate buffer allocations (single buffer reused for all chunks)
    /// - Returns ArraySegment (stack struct) to avoid heap allocations
    /// - Clears buffer on return to prevent data leaks between uploads
    /// </para>
    /// <para>
    /// CRITICAL SAFETY CONSTRAINT: This implementation uses a SINGLE pooled buffer that is reused
    /// for all chunks. The buffer is rented once at the start and returned only when enumeration completes.
    /// Each ArraySegment references this shared buffer.
    /// </para>
    /// <para>
    /// CALLER REQUIREMENTS:
    /// 1. MUST consume each chunk synchronously before the next iteration (await foreach guarantees this)
    /// 2. MUST NOT store ArraySegment references beyond the iteration body
    /// 3. MUST complete all async operations (hash + upload) before moving to next chunk
    /// </para>
    /// <para>
    /// Why this is safe in current usage:
    /// - await foreach ensures synchronous iteration (waits for each iteration to complete)
    /// - Caller immediately hashes the chunk (TransformBlock uses buffer synchronously)
    /// - Caller immediately uploads via MemoryStream (Azure SDK buffers data synchronously)
    /// - Both operations complete before next yield return is reached
    /// </para>
    /// <para>
    /// If Azure SDK behavior changes to read streams lazily/asynchronously, this could cause
    /// data corruption. Monitor Azure SDK updates and consider per-chunk buffer allocation if needed.
    /// </para>
    /// </remarks>
    private static IAsyncEnumerable<ArraySegment<byte>> _SplitStreamAsync(
        Stream sourceStream,
        int chunkSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sourceStream);
        Argument.IsPositive(chunkSize);
        // Validate chunk size doesn't exceed Azure's 100MB block limit
        Argument.IsLessThanOrEqualTo(chunkSize, 100 * 1024 * 1024);

        return enumerable(sourceStream, chunkSize, cancellationToken);

        static async IAsyncEnumerable<ArraySegment<byte>> enumerable(
            Stream sourceStream,
            int chunkSize,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            // Rent buffer from shared pool (reused for all chunks to minimize memory consumption)
            var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                while (true)
                {
                    var bytesRead = await sourceStream
                        .ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken)
                        .AnyContext();

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    // Return segment of the shared pooled buffer
                    // IMPORTANT: This segment is only valid until the next iteration!
                    // Caller MUST consume (hash + upload) before continuing enumeration
                    yield return new ArraySegment<byte>(buffer, 0, bytesRead);
                }
            }
            finally
            {
                // Return buffer to pool for reuse
                // clearArray: true ensures sensitive file data doesn't leak to other uploads
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    /// <summary>Calculates the optimal chunk size based on total data size and blob type</summary>
    /// <remarks>
    /// Strategy:
    /// - Unknown size: Use default chunk size
    /// - 0 bytes: Use default chunk size (edge case)
    /// - &lt; 10MB: Use minimum of default chunk or file size (avoid oversized chunks)
    /// - &lt; 100MB: Use default chunk size (4MB typically)
    /// - ≥ 100MB: Use max chunk size (100MB) for better throughput
    /// </remarks>
    private int _CalculateOptimalChunkSize(long? totalSize)
    {
        if (!totalSize.HasValue || totalSize.Value == 0)
        {
            return _options.BlobDefaultChunkSize;
        }

        return totalSize.Value switch
        {
            // (Less than 10MB) For small files, use smaller chunks to reduce memory usage
            < 10 * 1024 * 1024 => Math.Min(_options.BlobDefaultChunkSize, (int)totalSize.Value),
            // (Less than 100MB) For medium files, use default chunk size
            < 100 * 1024 * 1024 => _options.BlobDefaultChunkSize,
            // (100MB and above) For large files, use larger chunks for better performance
            _ => _options.BlobMaxChunkSize,
        };
    }

    /// <summary>Generates a unique, sortable block ID for Azure block blob uploads.</summary>
    private static string _GenerateBlockId(int blockIndex)
    {
        // Azure requires block IDs to be:
        // - Base64-encoded
        // - Unique within the blob
        // - Same length for all blocks (for proper sorting)

        // This method generates IDs in the format "block-{index:D10}" (e.g., "block-0000000000", "block-0000000001").
        // The padding ensures lexicographic sorting matches numeric ordering.

        return $"block-{blockIndex.ToString("D10", CultureInfo.InvariantCulture)}".ToBase64();
    }

    private async Task<HashAlgorithm?> _GetHasher(Stream stream, CancellationToken cancellationToken)
    {
        HashAlgorithm? hasher = null;

        try
        {
            var checksum = stream.GetUploadChecksumInfo();

            if (checksum is null)
            {
                return null;
            }

            hasher = _CreateHashAlgorithm(checksum.Algorithm);

            if (hasher is not null)
            {
                return hasher;
            }

            var supportedAlgorithms = await GetSupportedAlgorithmsAsync(cancellationToken);

            throw new NotSupportedException(
                $"Checksum algorithm '{checksum.Algorithm}' is not supported. Supported algorithms: {string.Join(", ", supportedAlgorithms)}"
            );
        }
        catch
        {
            hasher?.Dispose();

            throw;
        }
    }
}
