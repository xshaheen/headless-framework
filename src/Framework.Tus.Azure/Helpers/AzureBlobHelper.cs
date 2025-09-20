// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Framework.Tus.Models;
using Microsoft.Extensions.Logging;

namespace Framework.Tus.Helpers;

public class AzureBlobHelper(ILogger<AzureBlobHelper> logger)
{
    public string GetBlobName(string fileId, string blobPrefix)
    {
        return $"{blobPrefix.TrimEnd('/')}/{fileId}";
    }

    public async Task<bool> BlobExistsAsync(BlobClient blobClient, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await blobClient.ExistsAsync(cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<List<BlobBlock>> GetCommittedBlockListAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Create a new BlockBlobClient using the same URI and credentials
            var blockBlobClient = new Azure.Storage.Blobs.Specialized.BlockBlobClient(blobClient.Uri);
            var blockListResponse = await blockBlobClient.GetBlockListAsync(
                BlockListTypes.Committed,
                cancellationToken: cancellationToken
            );
            return blockListResponse.Value.CommittedBlocks.ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new List<BlobBlock>();
        }
    }

    public async Task<List<BlobBlock>> GetCommittedBlockListAsync(
        Azure.Storage.Blobs.Specialized.BlockBlobClient blockBlobClient,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var blockListResponse = await blockBlobClient.GetBlockListAsync(
                BlockListTypes.Committed,
                cancellationToken: cancellationToken
            );
            return blockListResponse.Value.CommittedBlocks.ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new List<BlobBlock>();
        }
    }

    public string GenerateBlockId(int blockNumber)
    {
        // Block IDs must be Base64 encoded and of equal length for proper ordering
        var blockId = $"block-{blockNumber:D10}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(blockId));
    }

    public async Task<bool> DeleteBlobIfExistsAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var response = await blobClient.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken
            );
            return response.Value;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete blob: {BlobName}", blobClient.Name);
            return false;
        }
    }
}
