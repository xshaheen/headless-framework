// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusCreationStore
{
    public async Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        var fileId = await _fileIdProvider.CreateId(metadata);

        try
        {
            // Metadata
            var blobMetadata = TusAzureMetadata.FromTus(metadata);

            blobMetadata.DateCreated = _timeProvider.GetUtcNow();
            blobMetadata.UploadLength = uploadLength;

            // Create empty blob with metadata and content type
            // This ensures the blob exists and has the correct metadata from the start
            // The actual data (blocks) will be uploaded in subsequent requests
            var blockBlobClient = _GetBlockBlobClient(fileId);
            await blockBlobClient.UploadAsync(
                content: Stream.Null,
                httpHeaders: await _blobHttpHeadersProvider.GetBlobHttpHeadersAsync(blobMetadata.ToUser()),
                metadata: blobMetadata.ToAzure(),
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("Created file {FileId} with upload length {UploadLength}", fileId, uploadLength);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create file with upload length {UploadLength}", uploadLength);

            throw;
        }
    }

    public async Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetTusFileInfoAsync(fileId, cancellationToken);

        return blobInfo?.Metadata.ToTusString();
    }
}
