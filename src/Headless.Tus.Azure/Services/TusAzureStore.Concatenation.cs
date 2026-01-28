// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs.Models;
using Headless.Checks;
using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models.Concatenation;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore : ITusConcatenationStore
{
    public async Task<FileConcat?> GetUploadConcatAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);
        var tusFile = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken);

        if (tusFile == null)
        {
            return null;
        }

        return tusFile.Metadata.ConcatType switch
        {
            "partial" => new FileConcatPartial(),
            "final" => new FileConcatFinal(tusFile.Metadata.PartialUploads),
            _ => null,
        };
    }

    public async Task<string> CreatePartialFileAsync(
        long uploadLength,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        var fileId = await _fileIdProvider.CreateId(metadata);
        var blobClient = _GetBlobClient(fileId);

        try
        {
            // Metadata
            var blobMetadata = TusAzureMetadata.FromTus(metadata);

            blobMetadata.DateCreated = _timeProvider.GetUtcNow();
            blobMetadata.UploadLength = uploadLength;
            blobMetadata.ConcatType = "partial";

            await blobClient.UploadAsync(
                content: Stream.Null,
                httpHeaders: await _blobHttpHeadersProvider.GetBlobHttpHeadersAsync(blobMetadata.ToUser()),
                metadata: blobMetadata.ToAzure(),
                cancellationToken: cancellationToken
            );

            _logger.LogInformation(
                "Created partial file {FileId} with upload length {UploadLength}",
                fileId,
                uploadLength
            );

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create partial file with upload length {UploadLength}", uploadLength);

            throw;
        }
    }

    public async Task<string> CreateFinalFileAsync(
        string[] partialFiles,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(partialFiles);

        var fileId = await _fileIdProvider.CreateId(metadata);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        try
        {
            // Validate all partial files exist and are complete
            await _ValidatePartialFilesAsync(partialFiles, cancellationToken);

            // Parse TUS metadata
            var blobMetadata = TusAzureMetadata.FromTus(metadata);

            // Calculate total size by concatenating all partial files
            long totalSize = 0;
            var blockIds = new List<string>();
            var blockNumber = 0;

            foreach (var partialFileId in partialFiles)
            {
                var partialBlobClient = _GetBlobClient(partialFileId);
                var partialBlockBlobClient = _GetBlockBlobClient(partialFileId);

                // Get the partial file's committed blocks
                var partialBlocks = await _GetCommittedBlocksAsync(partialBlockBlobClient, cancellationToken);

                foreach (var block in partialBlocks)
                {
                    // Copy each block from partial file to final file
                    var newBlockId = _GenerateBlockId(blockNumber++);

                    // Download block data from partial file
                    var blockRange = new HttpRange(totalSize, block.SizeLong);

                    var blockContent = await partialBlobClient.DownloadStreamingAsync(
                        new BlobDownloadOptions { Range = blockRange },
                        cancellationToken: cancellationToken
                    );

                    // Stage the block in the final file
                    await blockBlobClient.StageBlockAsync(
                        newBlockId,
                        blockContent.Value.Content,
                        cancellationToken: cancellationToken
                    );

                    blockIds.Add(newBlockId);
                    totalSize += block.SizeLong;
                }
            }

            // Create final file metadata
            blobMetadata.UploadLength = totalSize;
            blobMetadata.DateCreated = _timeProvider.GetUtcNow();
            blobMetadata.ConcatType = "final";
            blobMetadata.PartialUploads = partialFiles;

            // Commit all blocks to create the final file
            await blockBlobClient.CommitBlockListAsync(
                blockIds,
                metadata: blobMetadata.ToAzure(),
                cancellationToken: cancellationToken
            );

            _logger.LogInformation(
                "Created final file {FileId} by concatenating {PartialFileCount} partial files, total size: {TotalSize} bytes",
                fileId,
                partialFiles.Length,
                totalSize
            );

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to create final file from partial files: {PartialFiles}",
                string.Join(", ", partialFiles)
            );

            throw;
        }
    }

    private async Task _ValidatePartialFilesAsync(string[] partialFiles, CancellationToken cancellationToken)
    {
        foreach (var partialFileId in partialFiles)
        {
            var blobClient = _GetBlobClient(partialFileId);

            var blobInfo =
                await _GetTusFileInfoAsync(blobClient, partialFileId, cancellationToken)
                ?? throw new InvalidOperationException($"Partial file {partialFileId} does not exist");

            // Verify it's a partial file
            var concatType = blobInfo.Metadata.ConcatType;

            if (!string.Equals(concatType, "partial", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"File {partialFileId} is not a partial file");
            }

            // Verify the partial file is complete
            var uploadLength = blobInfo.Metadata.UploadLength;

            if (!uploadLength.HasValue)
            {
                throw new InvalidOperationException($"Partial file {partialFileId} has no upload length");
            }

            if (blobInfo.CurrentContentLength < uploadLength.Value)
            {
                FormattableString message =
                    $"Partial file {partialFileId} is incomplete ({blobInfo.CurrentContentLength}/{uploadLength.Value} bytes)";

                throw new InvalidOperationException(message.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
