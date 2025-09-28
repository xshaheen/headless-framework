// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusPipelineStore
{
    public async Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        return await AppendDataAsync(fileId, pipeReader.AsStream(), cancellationToken);

        // TODO: Implement PipeReader support for faster uploads

        // _logger.LogTrace("Appending data using the PipeReader for file '{FileId}'", fileId);
        //
        // var blobClient = _GetBlobClient(fileId);
        // var blockBlobClient = _GetBlockBlobClient(fileId);
        //
        // var blobInfo =
        //     await _GetBlobInfoAsync(blobClient, cancellationToken)
        //     ?? throw new InvalidOperationException($"File {fileId} does not exist");
        //
        // ReadResult result = default;
        // var bytesWrittenThisRequest = 0L;
        //
        // try
        // {
        //     long optimalPartSize = CalculateOptimalPartSize(s3UploadInfo.UploadLength);
        //
        //     while (!PipeReadingIsDone(result, cancellationToken))
        //     {
        //         result = await pipeReader.ReadAtLeastAsync((int)optimalPartSize, cancellationToken);
        //
        //         _AssertNotToMuchData(s3UploadInfo.UploadOffset, result.Buffer.Length, s3UploadInfo.UploadLength);
        //
        //         bytesWrittenThisRequest += await _tusS3Api.UploadPartData(
        //             s3UploadInfo,
        //             result.Buffer.AsStream(),
        //             cancellationToken
        //         );
        //
        //         _logger.LogDebug("Append '{PartialLength}' bytes to the file '{FileId}'", result.Buffer.Length, fileId);
        //
        //         pipeReader.AdvanceTo(result.Buffer.End);
        //     }
        //
        //     await pipeReader.CompleteAsync();
        // }
        // catch (Exception e)
        // {
        //     // Clear memory and complete the reader to not cause a
        //     // Microsoft.AspNetCore.Connections.ConnectionAbortedException inside Kestrel
        //     // later on as this is an "expected" exception.
        //     try
        //     {
        //         pipeReader.AdvanceTo(result.Buffer.End);
        //         await pipeReader.CompleteAsync();
        //     }
        //     #pragma warning disable ERP022 // Suppress "Avoid empty catch clause"
        //     catch
        //     {
        //         /* Ignore if we cannot complete the reader so that the real exception will propagate. */
        //     }
        //     #pragma warning restore ERP022
        //
        //     if (e is OperationCanceledException or TaskCanceledException)
        //     {
        //         _logger.LogWarning("Cancelled the upload operation for file id '{FileId}'", fileId);
        //     }
        //     else
        //     {
        //         throw;
        //     }
        // #pragma warning disable ERP022 // Suppress "Avoid empty catch clause"
        // }
        // #pragma warning restore ERP022
        //
        // return bytesWrittenThisRequest;
    }

    private static void _AssertNotToMuchData(
        long uploadOffsetLength,
        long numberOfBytesReadFromClient,
        long? fileUploadLengthProvidedDuringCreate
    )
    {
        var requestDataLength = uploadOffsetLength + numberOfBytesReadFromClient;

        if (requestDataLength > fileUploadLengthProvidedDuringCreate)
        {
            FormattableString message =
                $"Request contains more data than the file's upload length. Request data: {requestDataLength}, upload length: {fileUploadLengthProvidedDuringCreate}.";

            throw new TusStoreException(message.ToString(CultureInfo.InvariantCulture));
        }
    }
}
