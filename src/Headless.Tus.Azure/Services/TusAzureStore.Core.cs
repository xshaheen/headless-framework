// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs.Specialized;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore
{
    /// <summary>
    /// Returns <see langword="true"/> if a blob for the given TUS file identifier exists in the
    /// configured container.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns><see langword="true"/> if the blob exists; <see langword="false"/> otherwise</returns>
    public async Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        try
        {
            var response = await blobClient.ExistsAsync(cancellationToken);

            return response.Value;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the total declared upload length for the given TUS file, or
    /// <see langword="null"/> when deferred-length was used and the client has not yet provided
    /// the final size.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>declared upload length in bytes, or <see langword="null"/> if not yet known</returns>
    public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetTusFileInfoAsync(fileId, cancellationToken);

        return blobInfo?.Metadata.UploadLength;
    }

    /// <summary>
    /// Returns the number of bytes that have been durably committed for the given TUS file.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// the sum of all committed Azure block sizes in bytes; 0 if the blob does not exist or has
    /// no committed blocks
    /// </returns>
    /// <remarks>
    /// The offset is derived from the committed block list, not from the blob's
    /// <c>ContentLength</c> property. This correctly excludes any staged-but-uncommitted blocks
    /// (for example, blocks held pending checksum verification).
    /// </remarks>
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
