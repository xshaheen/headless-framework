// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Tus.Models;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusReadableStore
{
    public async Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);
        var blobInfo = await _GetBlobInfoAsync(blobClient, cancellationToken);

        if (blobInfo == null)
        {
            return null;
        }

        var tusFile = new TusAzureFile
        {
            FileId = fileId,
            BlobName = blobInfo.BlobName,
            UploadLength = _GetUploadLength(blobInfo.Metadata),
            UploadOffset = blobInfo.Size,
            Metadata = _DecodeMetadata(blobInfo.Metadata),
            ExpirationDate = _GetExpirationDate(blobInfo.Metadata),
            CreatedDate = _GetCreatedDate(blobInfo.Metadata),
            LastModified = blobInfo.LastModified,
        };

        return new TusAzureFileWrapper(tusFile, blobClient, _logger);
    }
}
