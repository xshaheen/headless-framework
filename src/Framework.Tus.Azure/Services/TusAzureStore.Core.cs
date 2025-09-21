// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs.Specialized;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore
{
    public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

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

    public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetTusFileInfoAsync(fileId, cancellationToken);

        return blobInfo?.Metadata.UploadLength;
    }

    public async Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetTusFileInfoAsync(fileId, cancellationToken);

        if (blobInfo == null)
        {
            return 0;
        }

        var blockBlobClient = _containerClient.GetBlockBlobClient(_GetBlobName(fileId));
        var blockList = await _GetCommittedBlocksAsync(blockBlobClient, cancellationToken);

        return blockList.Sum(block => block.SizeLong);
    }
}
