// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Specialized;
using Framework.Tus.Helpers;
using Framework.Tus.Models;
using Framework.Tus.Options;
using Microsoft.Extensions.Logging;

namespace Framework.Tus.Services;

public class BlockBlobHandler
{
    private readonly TusAzureStoreOptions _options;
    private readonly AzureBlobHelper _blobHelper;
    private readonly ILogger<BlockBlobHandler> _logger;

    public BlockBlobHandler(TusAzureStoreOptions options, AzureBlobHelper blobHelper, ILogger<BlockBlobHandler> logger)
    {
        _options = options;
        _blobHelper = blobHelper;
        _logger = logger;
    }

    public async Task CreateBlobAsync(
        BlockBlobClient blobClient,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken
    )
    {
        if (_options.HideBlobUntilComplete)
        {
            // Don't create the blob yet - it will only appear when committed
            // Just validate that we can create it by checking container access
            return;
        }

        // Create empty blob immediately for visibility (current implementation)
        var blobHttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = "application/octet-stream",
        };
        await blobClient.UploadAsync(
            Stream.Null,
            blobHttpHeaders,
            metadata: metadata,
            cancellationToken: cancellationToken
        );
    }

    public async Task<long> AppendDataAsync(
        BlockBlobClient blobClient,
        Stream data,
        TusAzureBlobInfo blobInfo,
        CancellationToken cancellationToken
    )
    {
        var totalBytesWritten = 0L;
        var maxChunkSize = _options.BlockBlobMaxChunkSize;
        var committedBlocks = await _blobHelper.GetCommittedBlockListAsync(blobClient, cancellationToken);
        var nextBlockNumber = committedBlocks.Count;

        if (_options.EnableChunkSplitting)
        {
            // Split large chunks into Azure-compatible sizes
            var newBlockIds = new List<string>();

            await foreach (var chunk in ChunkSplitterHelper.SplitStreamAsync(data, maxChunkSize, cancellationToken))
            {
                using (chunk)
                {
                    var blockId = _blobHelper.GenerateBlockId(nextBlockNumber++);
                    await blobClient.StageBlockAsync(blockId, chunk, cancellationToken: cancellationToken);
                    newBlockIds.Add(blockId);
                    totalBytesWritten += chunk.Length;
                }
            }

            // Commit blocks immediately for Azure-optimized approach
            var allBlockIds = committedBlocks.Select(b => b.Name).Concat(newBlockIds).ToList();
            await blobClient.CommitBlockListAsync(allBlockIds, cancellationToken: cancellationToken);

            // Update block count
            blobInfo.CommittedBlockCount = allBlockIds.Count;
        }
        else
        {
            // Direct staging and commit
            var blockId = _blobHelper.GenerateBlockId(nextBlockNumber);
            await blobClient.StageBlockAsync(blockId, data, cancellationToken: cancellationToken);

            var allBlockIds = committedBlocks.Select(b => b.Name).Concat([blockId]).ToList();
            await blobClient.CommitBlockListAsync(allBlockIds, cancellationToken: cancellationToken);

            totalBytesWritten = data.Length;
            blobInfo.CommittedBlockCount = allBlockIds.Count;
        }

        _logger.LogDebug(
            "Staged and committed {BytesWritten} bytes to block blob, total blocks: {BlockCount}",
            totalBytesWritten,
            blobInfo.CommittedBlockCount
        );

        return totalBytesWritten;
    }

    public async Task<long> GetUploadOffsetAsync(BlockBlobClient blobClient, CancellationToken cancellationToken)
    {
        var blockList = await _blobHelper.GetCommittedBlockListAsync(blobClient, cancellationToken);

        return blockList.Sum(block => block.SizeLong);
    }
}
