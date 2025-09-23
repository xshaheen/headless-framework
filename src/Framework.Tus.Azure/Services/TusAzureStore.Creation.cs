// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Constants;
using Framework.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;

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
            blobMetadata.BlockCount = 0;

            // Create empty blob with metadata and content type
            // This ensures the blob exists and has the correct metadata from the start
            // The actual data will be uploaded in subsequent requests
            var blockBlobClient = _containerClient.GetBlockBlobClient(_GetBlobName(fileId));

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
