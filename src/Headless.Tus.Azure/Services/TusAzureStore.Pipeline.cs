// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using tusdotnet.Extensions.Store;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore : ITusPipelineStore
{
    /// <summary>
    /// Reads upload data from a <c>PipeReader</c> and stages it as Azure Block Blob blocks,
    /// returning the number of bytes written in this request.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="pipeReader">the pipeline reader supplying PATCH request body data</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>bytes appended by this PATCH request</returns>
    /// <remarks>
    /// When the pipe carries a TUS-Checksum extension header (detected via
    /// <c>GetUploadChecksumInfo</c>), blocks are staged but <em>not</em> committed; the digest
    /// and block IDs are stored in blob metadata so that a subsequent <c>VerifyChecksumAsync</c>
    /// call can commit or discard them. Without a checksum header, all blocks are committed
    /// atomically alongside the updated metadata before returning.
    /// </remarks>
    /// <exception cref="InvalidOperationException">thrown if the file does not exist</exception>
    /// <exception cref="NotSupportedException">
    /// thrown if the client requests a checksum algorithm not in the supported list
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// thrown if <paramref name="fileId"/> or <paramref name="pipeReader"/> is null
    /// </exception>
    public async Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(fileId);
        Argument.IsNotNull(pipeReader);

        _logger.PipeReaderAppendStarted(fileId);

        var blobClient = _GetBlobClient(fileId);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        var azureFile =
            await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"File {fileId} does not exist");

        var committedBlocks = await _GetCommittedBlocksAsync(blockBlobClient, cancellationToken).ConfigureAwait(false);
        var currentOffset = committedBlocks.Sum(b => b.SizeLong);
        // currentOffset advances per staged chunk below; keep the pre-append offset as the
        // rollback point recorded with the commit (see the Stream overload for the rationale).
        var appendStartOffset = currentOffset;

        // Get checksum info if provided (from request headers via tusdotnet extension method)
        var checksumInfo = pipeReader.GetUploadChecksumInfo();
        using var hasher = checksumInfo is not null ? _CreateHasher(checksumInfo.Algorithm) : null;

        if (checksumInfo is not null && hasher is null)
        {
            var supportedAlgorithms = await GetSupportedAlgorithmsAsync(cancellationToken).ConfigureAwait(false);

            throw new NotSupportedException(
                $"Checksum algorithm '{checksumInfo.Algorithm}' is not supported. Supported algorithms: {string.Join(", ", supportedAlgorithms)}"
            );
        }

        var optimalChunkSize = _CalculateOptimalChunkSize(azureFile.Metadata.UploadLength);
        var nextBlockNumber = committedBlocks.Count;
        var blockToken = _NewBlockToken();
        var bytesWrittenThisRequest = 0L;
        var chunkBlockIds = new List<string>();

        ReadResult result = default;

        try
        {
            while (!_PipeReadingIsDone(result, cancellationToken))
            {
                // Read at least optimalChunkSize bytes, or whatever remains
                result = await pipeReader.ReadAtLeastAsync(optimalChunkSize, cancellationToken).ConfigureAwait(false);

                if (result.Buffer.IsEmpty)
                {
                    break;
                }

                // Validate we're not exceeding declared upload length
                _AssertNotToMuchData(currentOffset, result.Buffer.Length, azureFile.Metadata.UploadLength);

                // Process the buffer in optimal-sized chunks
                var buffer = result.Buffer;
                var consumed = buffer.Start;

                while (buffer.Length > 0)
                {
                    // Take up to optimalChunkSize bytes for this block
                    var chunkLength = (int)Math.Min(optimalChunkSize, buffer.Length);
                    var chunk = buffer.Slice(0, chunkLength);

                    // Calculate hash for this chunk if checksum verification is needed
                    if (hasher is not null)
                    {
                        _HashSequence(hasher, chunk);
                    }

                    // Stage the block
                    var blockId = _GenerateBlockId(blockToken, nextBlockNumber++);
                    await using var chunkStream = chunk.ToStream();
                    await blockBlobClient
                        .StageBlockAsync(blockId, chunkStream, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    chunkBlockIds.Add(blockId);
                    bytesWrittenThisRequest += chunkLength;
                    currentOffset += chunkLength;

                    // Advance to next chunk
                    buffer = buffer.Slice(chunkLength);
                    consumed = buffer.Start;
                }

                // Tell the PipeReader we've consumed up to this point
                pipeReader.AdvanceTo(consumed);

                _logger.BlocksStaged(bytesWrittenThisRequest, fileId, chunkBlockIds.Count);
            }

            await pipeReader.CompleteAsync().ConfigureAwait(false);

            // ATOMIC: Commit blocks + update metadata
            if (hasher is null)
            {
                // No checksum - commit immediately. Record the pre-append offset as the rollback point
                // for a later checksum-trailer verification and clear stale checksum-tracking state
                // (see the Stream overload for the rationale).
                List<string> allBlockIds = [.. committedBlocks.Select(b => b.Name), .. chunkBlockIds];
                _EnsureWithinBlockLimit(allBlockIds.Count);
                azureFile.Metadata.LastChunkBlocks = null;
                azureFile.Metadata.LastChunkChecksum = null;
                azureFile.Metadata.LastChunkOffset = appendStartOffset;
                var options = new CommitBlockListOptions { Metadata = azureFile.Metadata.ToAzure() };
                await blockBlobClient
                    .CommitBlockListAsync(allBlockIds, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // With checksum - store chunk info for later verification. The digest is prefixed with the
                // algorithm so VerifyChecksumAsync can confirm the requested algorithm matches the staged one.
                azureFile.Metadata.LastChunkBlocks = [.. chunkBlockIds];
                azureFile.Metadata.LastChunkChecksum =
                    $"{checksumInfo!.Algorithm}:{hasher.GetHashAndReset().ToBase64()}";
                azureFile.Metadata.LastChunkOffset = appendStartOffset;
                await _UpdateMetadataAsync(blobClient, azureFile, cancellationToken).ConfigureAwait(false);

                _logger.StoredPipelineChunkMetadata(fileId, chunkBlockIds.Count);
            }
        }
        catch (Exception e)
        {
            // Clear memory and complete the reader to prevent
            // Microsoft.AspNetCore.Connections.ConnectionAbortedException inside Kestrel
            try
            {
                pipeReader.AdvanceTo(result.Buffer.End);
                await pipeReader.CompleteAsync().ConfigureAwait(false);
            }
#pragma warning disable ERP022
            catch
            {
                // Ignore cleanup errors so the real exception propagates
            }
#pragma warning restore ERP022

            if (e is not OperationCanceledException and not TaskCanceledException)
            {
                throw;
            }

            _logger.UploadOperationCanceled(fileId);

            // CommitBlockListAsync / SetMetadata run only AFTER the read loop, so a cancellation mid-PATCH
            // committed nothing durable. Report 0 rather than the staged-but-uncommitted byte count, which
            // would otherwise tell the caller that bytes were persisted when GetUploadOffsetAsync (committed
            // blocks only) will report the old offset. The staged blocks are discarded by Azure's block GC.
#pragma warning disable ERP022 // Cancellation is intentionally observed and handled here, not swallowed silently.
            return 0;
#pragma warning restore ERP022
        }

        return bytesWrittenThisRequest;
    }

    /// <summary>
    /// Hashes a <see cref="ReadOnlySequence{T}"/> by feeding each segment's span to the incremental hash.
    /// </summary>
    private static void _HashSequence(IncrementalHash hasher, ReadOnlySequence<byte> sequence)
    {
        foreach (var segment in sequence)
        {
            hasher.AppendData(segment.Span);
        }
    }

    /// <summary>
    /// Determines if PipeReader reading should stop.
    /// </summary>
    private static bool _PipeReadingIsDone(ReadResult result, CancellationToken cancellationToken)
    {
        return result.IsCompleted || result.IsCanceled || cancellationToken.IsCancellationRequested;
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

internal static partial class TusAzureStorePipelineLog
{
    [LoggerMessage(
        EventId = 3226,
        Level = LogLevel.Trace,
        Message = "Appending data using the PipeReader for file '{FileId}'"
    )]
    public static partial void PipeReaderAppendStarted(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3227,
        Level = LogLevel.Debug,
        Message = "Staged {BytesWritten} bytes for file '{FileId}' ({BlockCount} blocks)"
    )]
    public static partial void BlocksStaged(this ILogger logger, long bytesWritten, string fileId, int blockCount);

    [LoggerMessage(
        EventId = 3228,
        Level = LogLevel.Debug,
        Message = "Stored chunk metadata for file '{FileId}': {BlockCount} blocks staged for checksum verification"
    )]
    public static partial void StoredPipelineChunkMetadata(this ILogger logger, string fileId, int blockCount);

    [LoggerMessage(
        EventId = 3229,
        Level = LogLevel.Warning,
        Message = "Cancelled the upload operation for file id '{FileId}'"
    )]
    public static partial void UploadOperationCanceled(this ILogger logger, string fileId);
}
