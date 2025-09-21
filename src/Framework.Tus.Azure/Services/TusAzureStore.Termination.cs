// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusTerminationStore
{
    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);

        bool deleted;

        try
        {
            var response = await blobClient.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken
            );

            deleted = response.Value;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete blob: {BlobName}", blobClient.Name);
            deleted = false;
        }

        if (deleted)
        {
            _logger.LogInformation("Deleted file {FileId}", fileId);
        }
        else
        {
            _logger.LogWarning("File {FileId} was not found for deletion", fileId);
        }
    }
}
