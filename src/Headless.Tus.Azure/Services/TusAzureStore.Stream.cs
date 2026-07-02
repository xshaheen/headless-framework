// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using tusdotnet.Extensions.Store;
using tusdotnet.Models;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore
{
    /// <summary>
    /// Reads upload data from a <c>Stream</c> and stages it as Azure Block Blob blocks,
    /// returning the number of bytes written in this request.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="stream">the stream supplying PATCH request body data</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>bytes appended by this PATCH request</returns>
    /// <remarks>
    /// Behavior mirrors the <c>PipeReader</c> overload: blocks are staged and committed
    /// atomically when no checksum is requested; when a TUS-Checksum header is present, blocks
    /// are staged only and the digest is stored in blob metadata pending
    /// <c>VerifyChecksumAsync</c>. When <c>EnableChunkSplitting</c> is
    /// <see langword="true"/>, the stream is split into fixed-size blocks using a pooled
    /// buffer; otherwise the entire stream is staged as one block.
    /// </remarks>
    /// <exception cref="TusStoreException">thrown if the file id is invalid or the file does not exist</exception>
    /// <exception cref="NotSupportedException">
    /// thrown if the client requests a checksum algorithm not in the supported list
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// thrown if <paramref name="fileId"/> or <paramref name="stream"/> is null
    /// </exception>
    public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(fileId);
        Argument.IsNotNull(stream);
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        _logger.StreamAppendStarted(fileId);

        var blobClient = _GetBlobClient(fileId);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        var azureFile =
            await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new TusStoreException($"File {fileId} does not exist");

        var committedBlocks = await _GetCommittedBlocksAsync(blockBlobClient, cancellationToken).ConfigureAwait(false);
        var currentOffset = committedBlocks.Sum(b => b.SizeLong);
        using var hasher = await _GetHasher(stream, cancellationToken).ConfigureAwait(false);

        // Stage blocks (with or without chunking)
        var (chunkBlockIds, bytesWritten) = await _StageAsync(
                blockBlobClient,
                stream,
                nextBlockNumber: committedBlocks.Count,
                azureFile.Metadata.UploadLength,
                currentOffset,
                hasher: hasher,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (chunkBlockIds.Count == 0)
        {
            await _RefreshChunkTrackingForEmptyAppendAsync(blobClient, azureFile, currentOffset).ConfigureAwait(false);

            return 0;
        }

        // ATOMIC: Commit blocks + update metadata in single operation for non-checksum uploads.
        // Must-complete (CancellationToken.None): on client disconnect the request token is already
        // cancelled, but the received bytes have to become durable so the client resumes from them
        // instead of re-uploading (TusDiskStore parity: reads use the live token, writes use None).
        if (hasher == null)
        {
            List<string> allBlockIds = [.. committedBlocks.Select(b => b.Name), .. chunkBlockIds];
            _EnsureWithinBlockLimit(allBlockIds.Count);

            // Record the pre-append offset as the rollback point: when the client sent Upload-Checksum
            // as an HTTP trailer, this store cannot see it during the append (no ChecksumAware wrapper),
            // so the data commits now and VerifyChecksumAsync verifies — and possibly rolls back — the
            // [LastChunkOffset, end) range afterwards. Also clear any stale checksum-tracking state from
            // a previous failed verification.
            azureFile.Metadata.LastChunkBlocks = null;
            azureFile.Metadata.LastChunkChecksum = null;
            azureFile.Metadata.LastChunkOffset = currentOffset;

            var options = new CommitBlockListOptions { Metadata = azureFile.Metadata.ToAzure() };
            await blockBlobClient
                .CommitBlockListAsync(allBlockIds, options, CancellationToken.None)
                .ConfigureAwait(false);

            return bytesWritten;
        }

        // Store the block IDs for this chunk - these are the blocks that will need to be committed or rolled back.
        // The digest is prefixed with the algorithm so VerifyChecksumAsync can confirm the requested algorithm
        // matches the one actually used to stage the data.
        var algorithm = stream.GetUploadChecksumInfo()!.Algorithm;
        azureFile.Metadata.LastChunkBlocks = [.. chunkBlockIds];
        azureFile.Metadata.LastChunkChecksum = $"{algorithm}:{hasher.GetHashAndReset().ToBase64()}";
        azureFile.Metadata.LastChunkOffset = currentOffset;
        // Must-complete: on disconnect the partial digest will not match the client's, so
        // verification discards the staged blocks — but only if this metadata write landed.
        await _UpdateMetadataAsync(blobClient, azureFile, CancellationToken.None).ConfigureAwait(false);

        _logger.StoredStreamChunkMetadata(fileId, chunkBlockIds.Count);

        return bytesWritten;
    }

    /// <summary>
    /// Stages blocks to the block blob, optionally splitting into chunks for large streams.
    /// </summary>
    private async Task<(List<string> BlockIds, long BytesWritten)> _StageAsync(
        BlockBlobClient blockBlobClient,
        Stream stream,
        int nextBlockNumber,
        long? fileUploadLength,
        long currentOffset,
        IncrementalHash? hasher,
        CancellationToken cancellationToken
    )
    {
        var blockToken = _NewBlockToken();

        if (!_options.EnableChunkSplitting)
        {
            var blockId = _GenerateBlockId(blockToken, nextBlockNumber);

            if (hasher is null) // No checksum, upload directly
            {
                if (stream.CanSeek)
                {
                    // Only the bytes from the current position are staged; count them the same way.
                    var remaining = stream.Length - stream.Position;
                    _AssertNotToMuchData(currentOffset, remaining, fileUploadLength);
                    await blockBlobClient
                        .StageBlockAsync(blockId, stream, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    return ([blockId], remaining);
                }

                // A non-seekable request body has no Length; buffer it so StageBlock has a content length and
                // we can report the exact bytes staged without ever calling Stream.Length on a forward-only stream.
                await using var buffered = new MemoryStream();

                try
                {
                    await stream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected mid-copy; persist what was received (spec: store as much
                    // of the received data as possible).
                }

                if (buffered.Length == 0)
                {
                    return ([], 0);
                }

                _AssertNotToMuchData(currentOffset, buffered.Length, fileUploadLength);
                buffered.Position = 0;
                await blockBlobClient
                    .StageBlockAsync(blockId, buffered, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                return ([blockId], buffered.Length);
            }

            // Read entire stream into MemoryStream for hashing and upload
            // Pre-allocate capacity if stream length is known to avoid resizing (clamped to int range)
            var capacity = stream is { CanSeek: true, Length: > 0 } ? (int)Math.Min(stream.Length, int.MaxValue) : 0;
            await using var memoryStream = new MemoryStream(capacity);

            try
            {
                await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected mid-copy; stage what was received — the partial digest will
                // fail verification and the staged blocks get discarded, matching the protocol.
            }

            if (memoryStream.Length == 0)
            {
                return ([], 0);
            }

            _AssertNotToMuchData(currentOffset, memoryStream.Length, fileUploadLength);

            hasher.AppendData(memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length));

            memoryStream.Position = 0; // Reuse MemoryStream for upload
            await blockBlobClient
                .StageBlockAsync(blockId, memoryStream, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            return ([blockId], memoryStream.Length);
        }

        var maxChunkSize = _CalculateOptimalChunkSize(fileUploadLength);

        // Pre-allocate list capacity to avoid resizing during iteration
        var estimatedChunkCount = fileUploadLength.HasValue
            ? (int)Math.Ceiling((double)fileUploadLength.Value / maxChunkSize)
            : 16; // Default capacity if length unknown

        var chunkBlockIds = new List<string>(estimatedChunkCount);
        var bytesWritten = 0L;

        await foreach (var chunk in _SplitStreamAsync(stream, maxChunkSize, cancellationToken).ConfigureAwait(false))
        {
            // Reject data beyond the declared upload length, mirroring the pipeline path's guard.
            _AssertNotToMuchData(currentOffset + bytesWritten, chunk.Count, fileUploadLength);

            var blockId = _GenerateBlockId(blockToken, nextBlockNumber++);

            // Calculate hash for this chunk if needed
            // AppendData consumes the buffer synchronously - safe with the shared pooled-buffer approach
            hasher?.AppendData(chunk.Array!, chunk.Offset, chunk.Count);

            // Upload the chunk as a block (must-complete: the bytes were already received from
            // the client, so staging must not abort on the disconnect-cancelled request token)
            // MemoryStream wrapper is necessary (Azure SDK requires Stream) but doesn't copy data
            // Azure SDK reads/buffers the stream synchronously - safe with shared buffer approach
            await using var chunkStream = new MemoryStream(chunk.Array!, chunk.Offset, chunk.Count, writable: false);
            await blockBlobClient
                .StageBlockAsync(blockId, chunkStream, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            chunkBlockIds.Add(blockId);
            bytesWritten += chunk.Count;
        }

        return (chunkBlockIds, bytesWritten);
    }

    /// <summary>Splits a stream into chunks of the specified maximum size.</summary>
    /// <param name="sourceStream">The source stream to read and split into chunks.</param>
    /// <param name="chunkSize">Maximum size of each chunk in bytes.</param>
    /// <param name="cancellationToken">Token to cancel the async enumeration.</param>
    /// <returns>Async enumerable of ArraySegments referencing portions of a pooled buffer.</returns>
    /// <remarks>
    /// <para>
    /// Performance optimizations:
    /// - Uses ArrayPool to eliminate buffer allocations (single buffer reused for all chunks)
    /// - Returns ArraySegment (stack struct) to avoid heap allocations
    /// - Clears buffer on return to prevent data leaks between uploads
    /// </para>
    /// <para>
    /// CRITICAL SAFETY CONSTRAINT: This implementation uses a SINGLE pooled buffer that is reused
    /// for all chunks. The buffer is rented once at the start and returned only when enumeration completes.
    /// Each ArraySegment references this shared buffer.
    /// </para>
    /// <para>
    /// CALLER REQUIREMENTS:
    /// 1. MUST consume each chunk synchronously before the next iteration (await foreach guarantees this)
    /// 2. MUST NOT store ArraySegment references beyond the iteration body
    /// 3. MUST complete all async operations (hash + upload) before moving to next chunk
    /// </para>
    /// <para>
    /// Why this is safe in current usage:
    /// - await foreach ensures synchronous iteration (waits for each iteration to complete)
    /// - Caller immediately hashes the chunk (IncrementalHash.AppendData consumes the buffer synchronously)
    /// - Caller immediately uploads via MemoryStream (Azure SDK buffers data synchronously)
    /// - Both operations complete before next yield return is reached
    /// </para>
    /// <para>
    /// If Azure SDK behavior changes to read streams lazily/asynchronously, this could cause
    /// data corruption. Monitor Azure SDK updates and consider per-chunk buffer allocation if needed.
    /// </para>
    /// </remarks>
    private static IAsyncEnumerable<ArraySegment<byte>> _SplitStreamAsync(
        Stream sourceStream,
        int chunkSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sourceStream);
        Argument.IsPositive(chunkSize);
        // Validate chunk size doesn't exceed Azure's 100MB block limit
        Argument.IsLessThanOrEqualTo(chunkSize, 100 * 1024 * 1024);

        return enumerable(sourceStream, chunkSize, cancellationToken);

        static async IAsyncEnumerable<ArraySegment<byte>> enumerable(
            Stream sourceStream,
            int chunkSize,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            // Rent buffer from shared pool (reused for all chunks to minimize memory consumption)
            var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                while (true)
                {
                    int bytesRead;

                    // The client read is the only place the live token belongs: tusdotnet's guarded
                    // stream usually surfaces a disconnect as EOF, but a token cancelled between
                    // reads throws — either way the bytes received so far must survive.
                    try
                    {
                        bytesRead = await sourceStream
                            .ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    // Return segment of the shared pooled buffer
                    // IMPORTANT: This segment is only valid until the next iteration!
                    // Caller MUST consume (hash + upload) before continuing enumeration
                    yield return new ArraySegment<byte>(buffer, 0, bytesRead);
                }
            }
            finally
            {
                // Return buffer to pool for reuse
                // clearArray: true ensures sensitive file data doesn't leak to other uploads
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    /// <summary>Calculates the optimal chunk size based on total data size and blob type</summary>
    /// <remarks>
    /// Strategy:
    /// - Unknown size: Use default chunk size
    /// - 0 bytes: Use default chunk size (edge case)
    /// - &lt; 10MB: Use minimum of default chunk or file size (avoid oversized chunks)
    /// - &lt; 100MB: Use default chunk size (4MB typically)
    /// - ≥ 100MB: Use max chunk size (100MB) for better throughput
    /// </remarks>
    private int _CalculateOptimalChunkSize(long? totalSize)
    {
        if (!totalSize.HasValue || totalSize.Value == 0)
        {
            return _options.BlobDefaultChunkSize;
        }

        return totalSize.Value switch
        {
            // (Less than 10MB) For small files, use smaller chunks to reduce memory usage
            < 10 * 1024 * 1024 => Math.Min(_options.BlobDefaultChunkSize, (int)totalSize.Value),
            // (Less than 100MB) For medium files, use default chunk size
            < 100 * 1024 * 1024 => _options.BlobDefaultChunkSize,
            // (100MB and above) For large files, use larger chunks for better performance
            _ => _options.BlobMaxChunkSize,
        };
    }

    /// <summary>
    /// A short random token, generated once per append/commit call, that scopes the block IDs of that call.
    /// </summary>
    /// <remarks>
    /// Block IDs are otherwise a pure function of the committed-block count, so two overlapping operations on the
    /// same blob (concurrent PATCHes, or a checksum-deferred PATCH followed by another append before the first is
    /// verified+committed) would generate identical IDs and silently overwrite each other's still-uncommitted
    /// blocks. The per-call token makes each call's staged IDs unique. 8 hex chars keeps the comma-joined
    /// <c>LastChunkBlocks</c> metadata well under Azure's 8&#160;KB per-blob metadata cap.
    /// </remarks>
    private static string _NewBlockToken()
    {
        return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
    }

    // Azure Block Blob caps a blob at 50,000 committed blocks. A long upload with a small chunk size can
    // approach it; fail with an actionable message rather than an opaque Azure error at commit time.
    private const int _MaxCommittedBlocks = 50_000;

    private static void _EnsureWithinBlockLimit(int blockCount)
    {
        if (blockCount <= _MaxCommittedBlocks)
        {
            return;
        }

        FormattableString message =
            $"Upload exceeds Azure's {_MaxCommittedBlocks}-block limit ({blockCount} blocks). Use a larger BlobDefaultChunkSize/BlobMaxChunkSize so the upload needs fewer, larger blocks.";

        throw new TusStoreException(message.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Generates a unique, sortable block ID for Azure block blob uploads.</summary>
    private static string _GenerateBlockId(string token, int blockIndex)
    {
        // Azure requires block IDs to be:
        // - Base64-encoded
        // - Unique within the blob
        // - Same length for all blocks (for proper sorting)

        // IDs are "block-{token}-{index:D10}" (e.g. "block-1a2b3c4d-0000000000"). The token (see _NewBlockToken)
        // guarantees uniqueness across overlapping calls; the zero-padded index preserves within-call ordering.
        return $"block-{token}-{blockIndex.ToString("D10", CultureInfo.InvariantCulture)}".ToBase64();
    }

    private async Task<IncrementalHash?> _GetHasher(Stream stream, CancellationToken cancellationToken)
    {
        IncrementalHash? hasher = null;

        try
        {
            var checksum = stream.GetUploadChecksumInfo();

            if (checksum is null)
            {
                return null;
            }

            hasher = _CreateHasher(checksum.Algorithm);

            if (hasher is not null)
            {
                return hasher;
            }

            var supportedAlgorithms = await GetSupportedAlgorithmsAsync(cancellationToken).ConfigureAwait(false);

            throw new NotSupportedException(
                $"Checksum algorithm '{checksum.Algorithm}' is not supported. Supported algorithms: {string.Join(", ", supportedAlgorithms)}"
            );
        }
        catch
        {
            hasher?.Dispose();

            throw;
        }
    }
}

internal static partial class TusAzureStoreStreamLog
{
    [LoggerMessage(
        EventId = 3224,
        Level = LogLevel.Trace,
        Message = "Appending data using the Stream for file '{FileId}'"
    )]
    public static partial void StreamAppendStarted(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3225,
        Level = LogLevel.Debug,
        Message = "Stored chunk metadata for file '{FileId}': {BlockCount} blocks staged for checksum verification"
    )]
    public static partial void StoredStreamChunkMetadata(this ILogger logger, string fileId, int blockCount);
}
