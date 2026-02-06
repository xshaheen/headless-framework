// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
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

            _logger.LogCreatedPartialFile(fileId, uploadLength);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogFailedToCreatePartialFile(e, uploadLength);

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

            // Concatenate all partial files
            long totalSize = 0;
            var blockIds = new List<string>();
            var blockNumber = 0;

            // Try server-side copy first (most performant - data stays in Azure)
            // Falls back to streaming if not supported (e.g., Azurite emulator)
            var useServerSideCopy = true;

            foreach (var partialFileId in partialFiles)
            {
                var partialBlobClient = _GetBlobClient(partialFileId);
                var partialBlockBlobClient = _GetBlockBlobClient(partialFileId);

                // Get the partial file's committed blocks
                var partialBlocks = await _GetCommittedBlocksAsync(partialBlockBlobClient, cancellationToken);

                // Track offset within this partial file (not across all files)
                long partialOffset = 0;

                foreach (var block in partialBlocks)
                {
                    var newBlockId = _GenerateBlockId(blockNumber++);
                    var blockRange = new HttpRange(partialOffset, block.SizeLong);

                    if (useServerSideCopy)
                    {
                        try
                        {
                            // Server-side copy: data stays in Azure, no download/upload
                            var options = new StageBlockFromUriOptions { SourceRange = blockRange };
                            await blockBlobClient.StageBlockFromUriAsync(
                                partialBlobClient.Uri,
                                newBlockId,
                                options,
                                cancellationToken
                            );
                        }
                        catch (RequestFailedException ex) when (ex.Status == 501)
                        {
                            // API not supported (e.g., Azurite) - fall back to streaming
                            useServerSideCopy = false;
                            _logger.LogStageBlockFromUriNotSupported();

                            await _StageBlockViaStreamingAsync(
                                blockBlobClient,
                                partialBlobClient,
                                newBlockId,
                                blockRange,
                                block.SizeLong,
                                cancellationToken
                            );
                        }
                    }
                    else
                    {
                        // Streaming fallback: download and re-upload
                        await _StageBlockViaStreamingAsync(
                            blockBlobClient,
                            partialBlobClient,
                            newBlockId,
                            blockRange,
                            block.SizeLong,
                            cancellationToken
                        );
                    }

                    blockIds.Add(newBlockId);
                    partialOffset += block.SizeLong;
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

            _logger.LogCreatedFinalFile(fileId, partialFiles.Length, totalSize);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogFailedToCreateFinalFile(e, string.Join(", ", partialFiles));

            throw;
        }
    }

    /// <summary>
    /// Stages a block by downloading from source and uploading to destination.
    /// Used as fallback when server-side copy is not available.
    /// </summary>
    private static async Task _StageBlockViaStreamingAsync(
        BlockBlobClient destinationClient,
        BlobClient sourceClient,
        string blockId,
        HttpRange sourceRange,
        long blockSize,
        CancellationToken cancellationToken
    )
    {
        var downloadResponse = await sourceClient.DownloadStreamingAsync(
            new BlobDownloadOptions { Range = sourceRange },
            cancellationToken
        );

        // Copy to MemoryStream since StageBlockAsync requires seekable stream with Length
        await using var buffer = new MemoryStream((int)blockSize);
        await downloadResponse.Value.Content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        await destinationClient.StageBlockAsync(blockId, buffer, cancellationToken: cancellationToken);
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
