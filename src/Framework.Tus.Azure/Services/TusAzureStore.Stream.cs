// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Azure.Storage.Blobs.Specialized;
using Framework.Checks;
using Microsoft.Extensions.Logging;

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

        var nextBlockNumber = committedBlocks.Count;
        var totalBytesWritten = 0L;

        if (_options.EnableChunkSplitting)
        {
            // Split large chunks into Azure-compatible sizes
            var maxChunkSize = _CalculateOptimalChunkSize(azureFile.Metadata.UploadLength);
            var newBlockIds = new List<string>();

            await foreach (var chunk in _SplitStreamAsync(stream, maxChunkSize, cancellationToken))
            {
                await using (chunk)
                {
                    var blockId = _GenerateBlockId(nextBlockNumber++);
                    await blockBlobClient.StageBlockAsync(blockId, chunk, cancellationToken: cancellationToken);
                    newBlockIds.Add(blockId);
                    totalBytesWritten += chunk.Length;
                }
            }

            // Commit blocks immediately for Azure-optimized approach
            var allBlockIds = committedBlocks.Select(b => b.Name).Concat(newBlockIds).ToList();
            await blockBlobClient.CommitBlockListAsync(allBlockIds, cancellationToken: cancellationToken);
        }
        else
        {
            // Direct staging and commit
            var blockId = _GenerateBlockId(nextBlockNumber);
            await blockBlobClient.StageBlockAsync(blockId, stream, cancellationToken: cancellationToken);

            var allBlockIds = committedBlocks.Select(b => b.Name).Concat([blockId]).ToList();
            await blockBlobClient.CommitBlockListAsync(allBlockIds, cancellationToken: cancellationToken);

            totalBytesWritten = stream.Length;
        }

        var bytesWritten = totalBytesWritten;

        // Update metadata with new block count
        var blockList = await _GetCommittedBlocksAsync(blockBlobClient, cancellationToken);
        azureFile.Metadata.BlockCount = blockList.Count;
        await _UpdateMetadataAsync(blobClient, azureFile, cancellationToken);

        return bytesWritten;
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

            var chunk = new MemoryStream();
            await chunk.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            chunk.Position = 0;

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

    private static string _GenerateBlockId(int blockNumber)
    {
        // Block IDs must be Base64 encoded and of equal length for proper ordering
        return $"block-{blockNumber.ToString("D10", CultureInfo.InvariantCulture)}".ToBase64();
    }
}
