// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using Headless.Checks;
using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Extensions.Store;
using tusdotnet.Interfaces;
using tusdotnet.Models;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Tus;

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
    /// When <c>EnableChunkSplitting</c> is <see langword="false"/> the whole PATCH body is buffered
    /// and staged as a single Azure block (matching the <c>Stream</c> overload) instead of being
    /// split into <c>optimalChunkSize</c> blocks.
    /// </remarks>
    /// <exception cref="TusStoreException">thrown if the file id is invalid or the file does not exist</exception>
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
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        _logger.PipeReaderAppendStarted(fileId);

        var blobClient = _GetBlobClient(fileId);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        var azureFile =
            await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new TusStoreException($"File {fileId} does not exist");

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

        // When EnableChunkSplitting is false the whole PATCH body is staged as ONE Azure block
        // (mirroring the Stream overload); otherwise the body is split into optimalChunkSize blocks.
        var enableSplitting = _options.EnableChunkSplitting;
        var maxNoSplitBuffer = _options.MaxNoSplitBufferSize;
        var optimalChunkSize = _CalculateOptimalChunkSize(azureFile.Metadata.UploadLength);
        var nextBlockNumber = committedBlocks.Count;
        var blockToken = _NewBlockToken();
        var bytesWrittenThisRequest = 0L;
        var chunkBlockIds = new List<string>();

        ReadResult result = default;

        // Accumulate read bytes into a buffer WE own (TusDiskStore's read/write-buffer design):
        // once the client disconnects, tusdotnet's guarded reader returns only empty IsCanceled
        // results (TryRead throws), so any bytes left inside the pipe are unrecoverable — data we
        // have read must be in our hands to satisfy "store as much of the received data as
        // possible". With splitting disabled the buffer grows to hold the entire PATCH body.
        var accumulationBuffer = ArrayPool<byte>.Shared.Rent(optimalChunkSize);
        var accumulatedCount = 0;

        try
        {
            var done = false;

            while (!done)
            {
                // Reads use the LIVE token: it is the only signal that the client is gone.
                // Everything that persists already-received bytes uses CancellationToken.None —
                // the token is exactly cancelled on disconnect (TusDiskStore parity: reads use the
                // live token, writes/flushes use None).
                try
                {
                    result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected; fall through and commit what was received.
                    _logger.UploadOperationCanceled(fileId);

                    break;
                }

                done = _PipeReadingIsDone(result, cancellationToken);

                var buffer = result.Buffer;

                while (!buffer.IsEmpty)
                {
                    // Split mode flushes a full optimalChunkSize block when the buffer fills; no-split
                    // mode grows the buffer instead (bounded by MaxNoSplitBufferSize), so the whole PATCH
                    // body ends up in a single block staged after the loop.
                    var capacity = enableSplitting
                        ? optimalChunkSize
                        : Math.Min(accumulationBuffer.Length, maxNoSplitBuffer);

                    if (accumulatedCount == capacity)
                    {
                        if (enableSplitting)
                        {
                            await stageAccumulatedAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            // Reject an over-length body early (known length) before buffering more, then
                            // grow within the cap. Without this the whole body buffers before the post-loop
                            // check, and for a deferred length (null) that check is a no-op — so the cap is
                            // the only in-flight memory bound.
                            _AssertNotToMuchData(currentOffset, accumulatedCount, azureFile.Metadata.UploadLength);
                            growAccumulationBuffer();
                            capacity = Math.Min(accumulationBuffer.Length, maxNoSplitBuffer);
                        }
                    }

                    var take = (int)Math.Min(buffer.Length, capacity - accumulatedCount);
                    // Copy into a buffer WE own rather than staging the ReadOnlySequence slice zero-copy:
                    // the slice is only valid until AdvanceTo below, and on disconnect those bytes are
                    // unrecoverable. The copy is a deliberate throughput-for-disconnect-safety trade — do
                    // not revert to zero-copy staging without preserving that invariant.
                    buffer.Slice(0, take).CopyTo(accumulationBuffer.AsSpan(accumulatedCount, take));
                    accumulatedCount += take;
                    buffer = buffer.Slice(take);
                }

                // Everything was copied into the accumulation buffer; consume it all.
                pipeReader.AdvanceTo(result.Buffer.End);

                _logger.BlocksStaged(bytesWrittenThisRequest, fileId, chunkBlockIds.Count);
            }

            // Final partial block (stream end or disconnect).
            if (accumulatedCount > 0)
            {
                await stageAccumulatedAsync().ConfigureAwait(false);
            }

            await pipeReader.CompleteAsync().ConfigureAwait(false);

            if (chunkBlockIds.Count == 0)
            {
                await _RefreshChunkTrackingForEmptyAppendAsync(blobClient, azureFile, appendStartOffset)
                    .ConfigureAwait(false);

                return 0;
            }

            // Commit the staged blocks now (no checksum) or defer them for a later checksum-trailer
            // verification via the shared helper (see the Stream overload) so the commit / defer /
            // rollback-offset protocol has exactly one implementation across both append paths.
            var deferred = await _CommitOrDeferChunkAsync(
                    blobClient,
                    blockBlobClient,
                    azureFile,
                    committedBlocks,
                    chunkBlockIds,
                    blockToken,
                    chunkStartOffset: appendStartOffset,
                    hasher,
                    checksumAlgorithm: checksumInfo?.Algorithm
                )
                .ConfigureAwait(false);

            if (deferred)
            {
                _logger.StoredPipelineChunkMetadata(fileId, chunkBlockIds.Count);
            }
        }
        catch
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

            throw;
        }
        finally
        {
            // clearArray: sensitive upload data must not leak to other pool users
            ArrayPool<byte>.Shared.Return(accumulationBuffer, clearArray: true);
        }

        return bytesWrittenThisRequest;

        // Stages the accumulated bytes as one block (must-complete: already received).
        async Task stageAccumulatedAsync()
        {
            _AssertNotToMuchData(currentOffset, accumulatedCount, azureFile.Metadata.UploadLength);

            hasher?.AppendData(accumulationBuffer, 0, accumulatedCount);

            var blockId = _GenerateBlockId(blockToken, nextBlockNumber++);
            await using var chunkStream = new MemoryStream(accumulationBuffer, 0, accumulatedCount, writable: false);
            await blockBlobClient
                .StageBlockAsync(blockId, chunkStream, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            chunkBlockIds.Add(blockId);
            bytesWrittenThisRequest += accumulatedCount;
            currentOffset += accumulatedCount;
            accumulatedCount = 0;
        }

        // Doubles the owned buffer (no-split mode only), clamped to MaxNoSplitBufferSize so a single
        // PATCH body cannot exhaust memory — the only in-flight bound when the upload length is deferred
        // (null), where _AssertNotToMuchData is a no-op.
        void growAccumulationBuffer()
        {
            if (accumulationBuffer.Length >= maxNoSplitBuffer)
            {
                FormattableString message =
                    $"A single PATCH body exceeds the {maxNoSplitBuffer}-byte no-split buffer cap (MaxNoSplitBufferSize). Enable chunk splitting, raise MaxNoSplitBufferSize, or send the upload in smaller PATCH requests.";

                throw new TusStoreException(message.ToString(CultureInfo.InvariantCulture));
            }

            var newSize = (int)Math.Min((long)accumulationBuffer.Length * 2, maxNoSplitBuffer);
            var larger = ArrayPool<byte>.Shared.Rent(newSize);
            accumulationBuffer.AsSpan(0, accumulatedCount).CopyTo(larger);
            // clearArray: the discarded buffer held upload data that must not leak to other pool users.
            ArrayPool<byte>.Shared.Return(accumulationBuffer, clearArray: true);
            accumulationBuffer = larger;
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
