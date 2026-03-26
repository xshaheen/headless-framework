// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Services;

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

            _logger.UploadLengthSet(fileId, uploadLength);
        }
        catch (Exception e)
        {
            _logger.UploadLengthSetFailed(e, fileId);

            throw;
        }
    }
}

internal static partial class TusAzureStoreCreationDeferLengthLog
{
    [LoggerMessage(
        EventId = 3202,
        Level = LogLevel.Debug,
        Message = "Set upload length for file {FileId} to {UploadLength}"
    )]
    public static partial void UploadLengthSet(this ILogger logger, string fileId, long uploadLength);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Error, Message = "Failed to set upload length for file {FileId}")]
    public static partial void UploadLengthSetFailed(this ILogger logger, Exception e, string fileId);
}
