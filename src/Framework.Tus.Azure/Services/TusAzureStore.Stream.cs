// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Checks;
using Microsoft.Extensions.Logging;
using tusdotnet.Extensions.Store;

namespace Framework.Tus.Services;

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
                Debug.Assert(stream.CanSeek, "Stream must be seekable in direct staging mode without checksum");
                await blockBlobClient.StageBlockAsync(blockId, stream, cancellationToken: cancellationToken);
                return ([blockId], stream.Length);
            }

            // Read entire stream into MemoryStream for hashing and upload
            await using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);

            memoryStream.Position = 0; // Reset position for hashing
            hasher.TransformFinalBlock(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);

            memoryStream.Position = 0; // Reuse MemoryStream for upload
            await blockBlobClient.StageBlockAsync(blockId, memoryStream, cancellationToken: cancellationToken);
            return ([blockId], memoryStream.Length);
        }

        var maxChunkSize = _CalculateOptimalChunkSize(fileUploadLength);
        var chunkBlockIds = new List<string>();
        var bytesWritten = 0L;

        await foreach (var chunk in _SplitStreamAsync(stream, maxChunkSize, cancellationToken))
        {
            await using (chunk)
            {
                var blockId = _GenerateBlockId(nextBlockNumber++);

                // Calculate hash for this chunk if needed
                if (hasher is not null)
                {
                    hasher.TransformBlock(chunk.GetBuffer(), 0, (int)chunk.Length, outputBuffer: null, 0);
                    chunk.Position = 0;
                }

                // Upload the chunk as a block (rewind after hashing consumed the stream)
                await blockBlobClient.StageBlockAsync(blockId, chunk, cancellationToken: cancellationToken);

                chunkBlockIds.Add(blockId);
                bytesWritten += chunk.Length;
            }
        }

        // Finalize hash if needed (TransformFinalBlock with empty data)
        hasher?.TransformFinalBlock([], 0, 0);

        return (chunkBlockIds, bytesWritten);
    }

    /// <summary>Splits a stream into chunks of the specified maximum size.</summary>
    private static async IAsyncEnumerable<MemoryStream> _SplitStreamAsync(
        Stream sourceStream,
        int chunkSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sourceStream);
        Argument.IsPositive(chunkSize);

        var buffer = new byte[chunkSize];

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            var chunk = new MemoryStream(bytesRead);
            await chunk.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            chunk.Position = 0; // Reset to start for reading

            yield return chunk;
        }
    }

    /// <summary>Calculates the optimal chunk size based on total data size and blob type</summary>
    private int _CalculateOptimalChunkSize(long? totalSize)
    {
        return totalSize switch
        {
            0 => _options.BlobDefaultChunkSize,
            // (Less than 10MB) For small files, use smaller chunks to reduce memory usage
            < 10 * 1024 * 1024 => Math.Min(_options.BlobDefaultChunkSize, (int)totalSize),
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
