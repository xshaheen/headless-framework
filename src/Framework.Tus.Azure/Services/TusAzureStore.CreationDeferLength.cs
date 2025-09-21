// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Framework.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusCreationDeferLengthStore
{
    public async Task SetUploadLengthAsync(string fileId, long uploadLength, CancellationToken cancellationToken)
    {
        try
        {
            var blobClient = _GetBlobClient(fileId);

            // Check if file exists
            var blobInfo =
                await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken)
                ?? throw new InvalidOperationException($"File {fileId} does not exist");

            // Update metadata
            blobInfo.Metadata.UploadLength = uploadLength;
            await _UpdateMetadataAsync(blobClient, blobInfo, cancellationToken);

            _logger.LogDebug("Set upload length for file {FileId} to {UploadLength}", fileId, uploadLength);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set upload length for file {FileId}", fileId);

            throw;
        }
    }
}
