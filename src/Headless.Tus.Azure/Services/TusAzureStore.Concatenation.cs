// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Checks;
using Headless.Tus.Models;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Tus;

public sealed partial class TusAzureStore : ITusConcatenationStore
{
    /// <summary>
    /// Returns the TUS concatenation type for the given file.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// a <c>FileConcatPartial</c> for partial uploads, a <c>FileConcatFinal</c> carrying the
    /// constituent partial file IDs for final uploads, or <see langword="null"/> if the file is a
    /// regular (non-concatenated) upload or does not exist
    /// </returns>
    public async Task<FileConcat?> GetUploadConcatAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobClient = _GetBlobClient(fileId);
        var tusFile = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Creates a new partial TUS file that will later be combined into a final upload via
    /// <c>CreateFinalFileAsync</c>.
    /// </summary>
    /// <param name="uploadLength">total bytes that will be uploaded to this partial file</param>
    /// <param name="metadata">
    /// raw TUS metadata string from the Upload-Metadata header, or <see langword="null"/>
    /// </param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the unique file identifier assigned to the new partial upload</returns>
    public async Task<string> CreatePartialFileAsync(
        long uploadLength,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        var fileId = await _fileIdProvider.CreateId(metadata).ConfigureAwait(false);
        var blobClient = _GetBlobClient(fileId);

        try
        {
            // Metadata
            var blobMetadata = TusAzureMetadata.FromTus(metadata);

            blobMetadata.DateCreated = _timeProvider.GetUtcNow();
            // Same Creation-Defer-Length contract as CreateFileAsync: -1 means "length not yet known".
            blobMetadata.UploadLength = uploadLength >= 0 ? uploadLength : null;
            blobMetadata.ConcatType = "partial";

            await blobClient
                .UploadAsync(
                    content: Stream.Null,
                    httpHeaders: await _blobHttpHeadersProvider
                        .GetBlobHttpHeadersAsync(blobMetadata.ToUser())
                        .ConfigureAwait(false),
                    metadata: blobMetadata.ToAzure(),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            _logger.LogCreatedPartialFile(fileId, uploadLength);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogFailedToCreatePartialFile(e, uploadLength);

            throw;
        }
    }

    /// <summary>
    /// Concatenates a set of completed partial uploads into a single final blob and returns the
    /// new file identifier.
    /// </summary>
    /// <param name="partialFiles">
    /// ordered array of partial TUS file identifiers to concatenate; must be non-empty and each
    /// file must already be fully uploaded
    /// </param>
    /// <param name="metadata">
    /// raw TUS metadata string for the resulting final file, or <see langword="null"/>
    /// </param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>the unique file identifier of the newly created final upload blob</returns>
    /// <remarks>
    /// Attempts server-side block copy (<c>StageBlockFromUri</c>) for efficiency; falls back to
    /// streaming download-and-re-upload when the operation is not supported (e.g., Azurite
    /// emulator). All partial files must be marked complete before calling this method.
    /// </remarks>
    /// <exception cref="ArgumentNullException">thrown if <paramref name="partialFiles"/> is null</exception>
    /// <exception cref="ArgumentException">thrown if <paramref name="partialFiles"/> is empty</exception>
    /// <exception cref="TusStoreException">
    /// thrown if any partial file id is invalid, does not exist, is not a partial upload, or has
    /// not been fully uploaded
    /// </exception>
    public async Task<string> CreateFinalFileAsync(
        string[] partialFiles,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(partialFiles);

        var fileId = await _fileIdProvider.CreateId(metadata).ConfigureAwait(false);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        try
        {
            // Validate all partial files exist and are complete. This is a separate pass from the copy below,
            // so a concurrent PATCH/Delete on a partial between validation and copy is a known TOCTOU window;
            // callers that mutate partials concurrently with concatenation must serialize via a lock provider.
            await _ValidatePartialFilesAsync(partialFiles, cancellationToken).ConfigureAwait(false);

            // Parse TUS metadata
            var blobMetadata = TusAzureMetadata.FromTus(metadata);

            // Concatenate all partial files
            long totalSize = 0;
            var blockIds = new List<string>();
            var blockNumber = 0;
            var blockToken = _NewBlockToken();

            // Try server-side copy first (most performant - data stays in Azure)
            // Falls back to streaming if not supported (e.g., Azurite emulator)
            var useServerSideCopy = true;

            foreach (var partialFileId in partialFiles)
            {
                var partialBlobClient = _GetBlobClient(partialFileId);
                var partialBlockBlobClient = _GetBlockBlobClient(partialFileId);

                // Get the partial file's committed blocks
                var partialBlocks = await _GetCommittedBlocksAsync(partialBlockBlobClient, cancellationToken)
                    .ConfigureAwait(false);

                // Track offset within this partial file (not across all files)
                long partialOffset = 0;

                foreach (var block in partialBlocks)
                {
                    var newBlockId = _GenerateBlockId(blockToken, blockNumber++);
                    var blockRange = new HttpRange(partialOffset, block.SizeLong);

                    if (useServerSideCopy)
                    {
                        try
                        {
                            // Server-side copy: data stays in Azure, no download/upload
                            var options = new StageBlockFromUriOptions { SourceRange = blockRange };
                            await blockBlobClient
                                .StageBlockFromUriAsync(partialBlobClient.Uri, newBlockId, options, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (RequestFailedException ex) when (ex.Status is 501 or 403)
                        {
                            // Server-side copy unavailable: 501 = not implemented (e.g. Azurite); 403 = the
                            // source blob is not readable via its bare URI (a private container needs a SAS on
                            // the source). Both are permanent for this deployment, so switch to streaming
                            // download+upload for this block and all remaining ones.
                            useServerSideCopy = false;
                            _logger.LogStageBlockFromUriNotSupported(ex.Status);

                            await _StageBlockViaStreamingAsync(
                                    blockBlobClient,
                                    partialBlobClient,
                                    newBlockId,
                                    blockRange,
                                    block.SizeLong,
                                    cancellationToken
                                )
                                .ConfigureAwait(false);
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
                            )
                            .ConfigureAwait(false);
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

            // A concat final persists tus_partial_uploads (comma-joined ids, unbounded count) on top of
            // the verbatim Upload-Metadata, so the composed metadata can exceed Azure's 8 KB cap even
            // though each part passed its own guard. Fail with an actionable message instead of the
            // opaque Azure 400 the commit below would otherwise raise.
            blobMetadata.EnsureWithinAzureMetadataLimit();

            // Commit all blocks to create the final file, applying the same content-type/HTTP headers the
            // regular and partial create paths set (CreateFileAsync / CreatePartialFileAsync).
            _EnsureWithinBlockLimit(blockIds.Count);
            var commitOptions = new CommitBlockListOptions
            {
                Metadata = blobMetadata.ToAzure(),
                HttpHeaders = await _blobHttpHeadersProvider
                    .GetBlobHttpHeadersAsync(blobMetadata.ToUser())
                    .ConfigureAwait(false),
            };
            await blockBlobClient
                .CommitBlockListAsync(blockIds, commitOptions, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogCreatedFinalFile(fileId, partialFiles.Length, totalSize);

            if (_options.DeletePartialFilesOnConcat)
            {
                await _DeletePartialFilesBestEffortAsync(partialFiles).ConfigureAwait(false);
            }

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
        var downloadResponse = await sourceClient
            .DownloadStreamingAsync(new BlobDownloadOptions { Range = sourceRange }, cancellationToken)
            .ConfigureAwait(false);

        // BlobDownloadStreamingResult owns the live network stream; dispose it so the connection is
        // released even on the upload path below. Block size is bounded by Azure's 100 MB block limit.
        using var download = downloadResponse.Value;

        // Copy to MemoryStream since StageBlockAsync requires seekable stream with Length
        await using var buffer = new MemoryStream((int)blockSize);
        await download.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        await destinationClient
            .StageBlockAsync(blockId, buffer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the partial uploads that formed a final upload. Best-effort on
    /// <c>CancellationToken.None</c>: the final blob is already committed, so a failed or aborted
    /// deletion must neither fail the request nor stop the remaining deletions.
    /// </summary>
    private async Task _DeletePartialFilesBestEffortAsync(string[] partialFiles)
    {
        // The same partial may be listed multiple times in one Upload-Concat; delete each once.
        foreach (var partialFileId in partialFiles.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await DeleteFileAsync(partialFileId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogFailedToDeletePartialFileAfterConcat(e, partialFileId);
            }
        }
    }

    private async Task _ValidatePartialFilesAsync(string[] partialFiles, CancellationToken cancellationToken)
    {
        // The tus Concatenation extension explicitly allows a partial to "be used multiple times to
        // form a final resource", including repeated entries in one Upload-Concat list — each
        // occurrence is copied again, so no duplicate check here.
        //
        // All failures throw TusStoreException (tusdotnet maps it to 400 + message): these are
        // client-caused states, and tusdotnet's own UploadConcatForConcatenateFiles requirement
        // pre-validates them per request — reaching one here means a TOCTOU race or store-direct use.
        foreach (var partialFileId in partialFiles)
        {
            await _EnsureValidFileIdAsync(partialFileId).ConfigureAwait(false);

            var blobClient = _GetBlobClient(partialFileId);

            var blobInfo =
                await _GetTusFileInfoAsync(blobClient, partialFileId, cancellationToken).ConfigureAwait(false)
                ?? throw new TusStoreException($"Partial file {partialFileId} does not exist");

            // Verify it's a partial file
            var concatType = blobInfo.Metadata.ConcatType;

            if (!string.Equals(concatType, "partial", StringComparison.Ordinal))
            {
                throw new TusStoreException($"File {partialFileId} is not a partial file");
            }

            // Verify the partial file is complete
            var uploadLength = blobInfo.Metadata.UploadLength;

            if (!uploadLength.HasValue)
            {
                throw new TusStoreException($"Partial file {partialFileId} has no upload length");
            }

            if (blobInfo.CurrentContentLength < uploadLength.Value)
            {
                FormattableString message =
                    $"Partial file {partialFileId} is incomplete ({blobInfo.CurrentContentLength}/{uploadLength.Value} bytes)";

                throw new TusStoreException(message.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
