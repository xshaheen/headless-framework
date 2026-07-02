// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Helpers;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus.Services;

public sealed partial class TusAzureStore : ITusChecksumStore
{
    private readonly IEnumerable<string> _cachedSupportedAlgorithms = ["sha1", "sha256", "sha512", "md5"];

    private readonly Dictionary<string, HashAlgorithmName> _supportedAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        { "sha1", HashAlgorithmName.SHA1 },
        { "sha256", HashAlgorithmName.SHA256 },
        { "sha512", HashAlgorithmName.SHA512 },
        { "md5", HashAlgorithmName.MD5 },
    };

    /// <summary>
    /// Returns the hash algorithms supported for TUS checksum verification.
    /// </summary>
    /// <param name="cancellationToken">token to cancel the operation (not used; result is pre-cached)</param>
    /// <returns>algorithm names: <c>sha1</c>, <c>sha256</c>, <c>sha512</c>, <c>md5</c></returns>
    public Task<IEnumerable<string>> GetSupportedAlgorithmsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_cachedSupportedAlgorithms);
    }

    /*
     * The Client and the Server MAY implement and use this extension (Checksum)
     * to verify data integrity of each PATCH request. Two flows reach this store:
     *
     * 1. Upload-Checksum HEADER: tusdotnet wraps the request body in a ChecksumAware stream/pipe,
     *    so AppendDataAsync sees the algorithm up-front, stages the blocks WITHOUT committing, and
     *    records the running digest in metadata. VerifyChecksumAsync compares the digests and
     *    commits (match) or leaves the blocks uncommitted (mismatch; Azure GC discards them).
     *
     * 2. Upload-Checksum TRAILER: the checksum is not known until after the body was read, so
     *    AppendDataAsync sees no checksum info and commits immediately — recording the pre-append
     *    offset (tus_last_chunk_offset) as a rollback point. VerifyChecksumAsync then hashes the
     *    committed [LastChunkOffset, end) range on demand and, on mismatch (or tusdotnet's
     *    faulty-trailer fallback sentinel), rolls the blob back by re-committing the previous
     *    block list — the Azure analog of TusDiskStore's SetLength(chunkStartPosition).
     */

    /// <summary>
    /// Verifies the checksum of the most recent PATCH against the digest supplied by the client.
    /// For header-based checksums this compares the digest pre-computed during
    /// <c>AppendDataAsync</c> and commits the staged blocks on success; for trailer-based
    /// checksums it hashes the last chunk's committed range on demand and rolls the chunk back
    /// on mismatch.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="algorithm">hash algorithm name as reported in the TUS-Checksum header (e.g. <c>sha256</c>)</param>
    /// <param name="checksum">expected digest bytes supplied by the client</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// <see langword="true"/> if the digest matches and the chunk is durably committed;
    /// <see langword="false"/> on mismatch or missing state — the chunk is discarded (staged
    /// blocks left uncommitted, or the committed range rolled back)
    /// </returns>
    /// <remarks>
    /// Comparison uses <c>CryptographicOperations.FixedTimeEquals</c> to prevent timing attacks.
    /// <paramref name="cancellationToken"/> is deliberately ignored for all store I/O (mirroring
    /// <c>TusDiskStore</c>): tusdotnet passes the request's token, which is <em>already
    /// cancelled</em> when the client disconnected — including right after sending a checksum
    /// trailer over data this store has already committed. Honoring the token there could abort
    /// between verification and rollback and leave an unverified (possibly corrupt) chunk durable,
    /// so verification and its cleanup are must-complete.
    /// </remarks>
    public async Task<bool> VerifyChecksumAsync(
        string fileId,
        string algorithm,
        byte[] checksum,
        CancellationToken cancellationToken
    )
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobClient = _GetBlobClient(fileId);
        var blockBlobClient = _GetBlockBlobClient(fileId);

        // tusdotnet substitutes this sentinel when the checksum-trailer was faulty or the client
        // disconnected before sending it; the store must discard the chunk unconditionally.
        var isFallback = ChecksumTrailerHelper.IsFallback(algorithm, checksum);

        TusAzureFile file;

        try
        {
            var info = await _GetTusFileInfoAsync(blobClient, fileId, CancellationToken.None).ConfigureAwait(false);

            if (info is null)
            {
                _logger.ChecksumVerificationFileInfoNotFound(fileId);
                return false;
            }

            file = info;
        }
        catch (Exception e)
        {
            _logger.ChecksumVerificationFailedUnexpectedly(e, fileId, algorithm);

            return false;
        }

        if (isFallback)
        {
            _logger.TrailerFallbackChunkDiscarded(fileId);
            await _DiscardLastChunkAsync(blobClient, blockBlobClient, file).ConfigureAwait(false);

            return false;
        }

        // Header flow: the digest was pre-computed during AppendDataAsync and the blocks are staged
        // but uncommitted.
        if (!string.IsNullOrEmpty(file.Metadata.LastChunkChecksum))
        {
            byte[] calculatedChecksum;

            try
            {
                if (!_TryGetChecksum(file, algorithm, out var stored))
                {
                    return false;
                }

                calculatedChecksum = stored;
            }
            catch (Exception e)
            {
                _logger.ChecksumVerificationFailedUnexpectedly(e, fileId, algorithm);

                return false;
            }

            if (!_VerifyChecksum(calculatedChecksum, checksum))
            {
                _logger.ChecksumVerificationFailed(fileId, algorithm, checksum, calculatedChecksum);

                // Don't commit the last chunk, effectively discarding it (since it's not part of the
                // committed blob) as the TUS protocol requires.
                return false;
            }

            _logger.ChecksumVerificationPassed(fileId, algorithm);

            // Commit OUTSIDE the catch-all above: a failure here is an infrastructure error (throttling,
            // network), NOT a checksum mismatch. Let it propagate so the client retries the verification,
            // instead of being told via a false return that its (correctly-checksummed) data was corrupt
            // and discarding it.
            await _CommitLastChunkAsync(blockBlobClient, file, CancellationToken.None).ConfigureAwait(false);

            return true;
        }

        // Trailer flow: the chunk is already committed; verify its range on demand. Must-complete:
        // an abort between hashing and rollback would leave an unverified chunk durable.
        return await _VerifyCommittedLastChunkAsync(blobClient, blockBlobClient, file, algorithm, checksum)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies an already-committed last chunk (checksum-trailer flow) by hashing its
    /// <c>[LastChunkOffset, end)</c> range, rolling the chunk back on mismatch. Runs entirely on
    /// <c>CancellationToken.None</c>: the caller's request token may already be cancelled, and
    /// aborting between hashing and rollback would leave an unverified chunk durable.
    /// </summary>
    private async Task<bool> _VerifyCommittedLastChunkAsync(
        BlobClient blobClient,
        BlockBlobClient blockBlobClient,
        TusAzureFile file,
        string algorithm,
        byte[] checksum
    )
    {
        var chunkStart = file.Metadata.LastChunkOffset;

        if (chunkStart is null)
        {
            // Nothing was appended (VerifyChecksumAsync without a prior AppendDataAsync) or the blob
            // predates rollback-point tracking; there is no verifiable chunk.
            _logger.NoVerifiableChunk(file.FileId);

            return false;
        }

        var chunkLength = file.CurrentContentLength - chunkStart.Value;

        if (chunkLength < 0)
        {
            _logger.LastChunkStateCorrupted(file.FileId, chunkStart.Value, file.CurrentContentLength);

            return false;
        }

        // Infrastructure failures (download/hash) propagate rather than returning false: a false
        // return tells the client its data was corrupt and discarded, which is untrue here.
        var calculatedChecksum = await _ComputeCommittedRangeChecksumAsync(
                blobClient,
                algorithm,
                chunkStart.Value,
                chunkLength,
                CancellationToken.None
            )
            .ConfigureAwait(false);

        if (calculatedChecksum is null)
        {
            // Unsupported algorithm; tusdotnet validates against GetSupportedAlgorithmsAsync up-front,
            // so this only happens for store-direct callers.
            return false;
        }

        if (_VerifyChecksum(calculatedChecksum, checksum))
        {
            _logger.ChecksumVerificationPassed(file.FileId, algorithm);

            return true;
        }

        _logger.ChecksumVerificationFailed(file.FileId, algorithm, checksum, calculatedChecksum);
        await _RollbackLastChunkAsync(blockBlobClient, file, chunkStart.Value).ConfigureAwait(false);

        return false;
    }

    /// <summary>
    /// Discards the most recent chunk after a faulty/missing checksum-trailer: staged-but-uncommitted
    /// blocks are dropped by clearing their tracking metadata (Azure GC reaps them), a committed
    /// chunk is rolled back to its recorded start offset.
    /// </summary>
    private async Task _DiscardLastChunkAsync(BlobClient blobClient, BlockBlobClient blockBlobClient, TusAzureFile file)
    {
        if (file.Metadata.LastChunkBlocks is { Count: > 0 })
        {
            file.Metadata.LastChunkBlocks = null;
            file.Metadata.LastChunkChecksum = null;
            await blobClient
                .SetMetadataAsync(file.Metadata.ToAzure(), cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            return;
        }

        var chunkStart = file.Metadata.LastChunkOffset;

        if (chunkStart is null || chunkStart.Value >= file.CurrentContentLength)
        {
            // Nothing committed by the last append — nothing to discard.
            return;
        }

        await _RollbackLastChunkAsync(blockBlobClient, file, chunkStart.Value).ConfigureAwait(false);
    }

    /// <summary>
    /// Rolls the blob back to <paramref name="chunkStartOffset"/> by re-committing the prefix of the
    /// current committed block list — the Azure equivalent of <c>TusDiskStore</c>'s
    /// <c>SetLength(chunkStartPosition)</c>. Blocks dropped from the list are garbage-collected by Azure.
    /// </summary>
    /// <remarks>
    /// Runs on <c>CancellationToken.None</c>: rollback is must-complete cleanup, and the caller's
    /// request token is already cancelled when the client disconnected before sending the trailer.
    /// </remarks>
    private async Task _RollbackLastChunkAsync(BlockBlobClient client, TusAzureFile file, long chunkStartOffset)
    {
        var committedBlocks = await _GetCommittedBlocksAsync(client, CancellationToken.None).ConfigureAwait(false);

        var prefix = new List<string>(committedBlocks.Count);
        var cumulative = 0L;

        foreach (var block in committedBlocks)
        {
            if (cumulative >= chunkStartOffset)
            {
                break;
            }

            prefix.Add(block.Name);
            cumulative += block.SizeLong;
        }

        if (cumulative != chunkStartOffset)
        {
            // Block boundaries no longer align with the recorded chunk start; refuse to guess a
            // rollback point rather than corrupt the upload.
            _logger.LastChunkStateCorrupted(file.FileId, chunkStartOffset, cumulative);

            throw new TusStoreException(
                $"Cannot roll back file {file.FileId}: committed blocks do not align with the recorded chunk offset {chunkStartOffset}."
            );
        }

        file.Metadata.LastChunkBlocks = null;
        file.Metadata.LastChunkChecksum = null;
        file.Metadata.LastChunkOffset = null;

        // HttpHeaders must be re-supplied: Put Block List clears any x-ms-blob-* property omitted
        // from the request, which would wipe creation-time headers during rollback.
        var options = new CommitBlockListOptions { Metadata = file.Metadata.ToAzure(), HttpHeaders = file.HttpHeaders };
        await client.CommitBlockListAsync(prefix, options, CancellationToken.None).ConfigureAwait(false);

        _logger.LastChunkRolledBack(file.FileId, chunkStartOffset);
    }

    /// <summary>
    /// Streams the committed range <c>[from, from + length)</c> and returns its digest, or
    /// <see langword="null"/> when the algorithm is not supported.
    /// </summary>
    private async Task<byte[]?> _ComputeCommittedRangeChecksumAsync(
        BlobClient blobClient,
        string algorithm,
        long from,
        long length,
        CancellationToken cancellationToken
    )
    {
        using var hasher = _CreateHasher(algorithm);

        if (hasher is null)
        {
            return null;
        }

        if (length == 0)
        {
            return hasher.GetHashAndReset();
        }

        var response = await blobClient
            .DownloadStreamingAsync(new BlobDownloadOptions { Range = new HttpRange(from, length) }, cancellationToken)
            .ConfigureAwait(false);

        using var download = response.Value;
        var buffer = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            int bytesRead;

            while (
                (
                    bytesRead = await download
                        .Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false)
                ) > 0
            )
            {
                hasher.AppendData(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        return hasher.GetHashAndReset();
    }

    private IncrementalHash? _CreateHasher(string algorithm)
    {
        return _supportedAlgorithms.TryGetValue(algorithm, out var name) ? IncrementalHash.CreateHash(name) : null;
    }

    private bool _TryGetChecksum(TusAzureFile file, string requestedAlgorithm, [NotNullWhen(true)] out byte[]? checksum)
    {
        // The digest is stored in metadata (LastChunkChecksum) during AppendDataAsync as
        // "{algorithm}:{base64digest}". If missing, it indicates either:
        // - A bug in the upload flow (checksum should have been calculated)
        // - Corrupted metadata

        checksum = null;
        var lastChunkChecksum = file.Metadata.LastChunkChecksum;

        if (string.IsNullOrEmpty(lastChunkChecksum))
        {
            _logger.PreCalculatedChecksumMissing(file.FileId);

            return false;
        }

        var separator = lastChunkChecksum.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0)
        {
            _logger.InvalidStoredChecksumFormat(new FormatException("Missing algorithm prefix."), file.FileId);

            return false;
        }

        // Confirm the algorithm the client is verifying with is the one actually used to stage the data,
        // rather than relying on the digest bytes happening to be equal-length.
        var storedAlgorithm = lastChunkChecksum[..separator];

        if (!string.Equals(storedAlgorithm, requestedAlgorithm, StringComparison.OrdinalIgnoreCase))
        {
            _logger.ChecksumAlgorithmMismatch(file.FileId, requestedAlgorithm, storedAlgorithm);

            return false;
        }

        try
        {
            checksum = Convert.FromBase64String(lastChunkChecksum[(separator + 1)..]);

            _logger.UsingPreCalculatedChecksum(file.FileId);
        }
        catch (FormatException e)
        {
            _logger.InvalidStoredChecksumFormat(e, file.FileId);

            return false;
        }

        return true;
    }

    private static bool _VerifyChecksum(byte[]? calculatedChecksum, byte[]? expectedChecksum)
    {
        if (calculatedChecksum == null || expectedChecksum == null)
        {
            return false;
        }

        if (calculatedChecksum.Length != expectedChecksum.Length)
        {
            return false;
        }

        // Uses CryptographicOperations.FixedTimeEquals to prevent timing-based side-channel attacks.
        // A naive byte-by-byte comparison could leak information about where the mismatch occurs,
        // potentially allowing an attacker to forge checksums.
        return CryptographicOperations.FixedTimeEquals(calculatedChecksum, expectedChecksum);
    }

    private async Task _CommitLastChunkAsync(BlockBlobClient client, TusAzureFile file, CancellationToken token)
    {
        // CRITICAL: This method performs atomic operations to ensure consistency:
        // 1. Reads committed blocks from Azure
        // 2. Merges with LastChunkBlocks from metadata (staged blocks)
        // 3. Clears chunk tracking metadata (LastChunkBlocks, LastChunkChecksum)
        // 4. Commits block list + metadata in single Azure operation

        try
        {
            var committedBlocks = await _GetCommittedBlocksAsync(client, token).ConfigureAwait(false);

            // Build complete block list BEFORE clearing metadata: the staged IDs are reconstructed
            // from the persisted (token, firstIndex, count) triple — see TusStagedBlocks.
            var staged = file.Metadata.LastChunkBlocks;
            var allBlockIds = new List<string>(committedBlocks.Count + (staged?.Count ?? 0));
            allBlockIds.AddRange(committedBlocks.Select(x => x.Name));

            if (staged is { } stagedRange)
            {
                for (var i = 0; i < stagedRange.Count; i++)
                {
                    allBlockIds.Add(_GenerateBlockId(stagedRange.Token, stagedRange.FirstIndex + i));
                }
            }

            _EnsureWithinBlockLimit(allBlockIds.Count);

            // NOW clear chunk tracking metadata (after we've used it)
            file.Metadata.LastChunkBlocks = null;
            file.Metadata.LastChunkChecksum = null;

            // ATOMIC: Commit blocks + update metadata in single operation. HttpHeaders must be
            // re-supplied: Put Block List clears any x-ms-blob-* property omitted from the request.
            var options = new CommitBlockListOptions
            {
                Metadata = file.Metadata.ToAzure(),
                HttpHeaders = file.HttpHeaders,
            };
            await client.CommitBlockListAsync(allBlockIds, options, cancellationToken: token).ConfigureAwait(false);

            _logger.LastChunkCommitted(file.FileId, allBlockIds.Count);
        }
        catch (Exception e)
        {
            _logger.CommitLastChunkFailed(e, file.FileId);

            throw;
        }
    }
}

internal static partial class TusAzureStoreChecksumLog
{
    [LoggerMessage(
        EventId = 3230,
        Level = LogLevel.Error,
        Message = "File info not found for {FileId} during checksum verification"
    )]
    public static partial void ChecksumVerificationFileInfoNotFound(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3231,
        Level = LogLevel.Debug,
        Message = "Checksum verification passed for file {FileId} using algorithm {Algorithm}"
    )]
    public static partial void ChecksumVerificationPassed(this ILogger logger, string fileId, string algorithm);

    public static void ChecksumVerificationFailed(
        this ILogger logger,
        string fileId,
        string algorithm,
        byte[] expected,
        byte[] calculated
    )
    {
        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        logger.ChecksumVerificationFailedCore(
            fileId,
            algorithm,
            Convert.ToHexString(expected),
            Convert.ToHexString(calculated)
        );
    }

    [LoggerMessage(
        EventId = 3232,
        Level = LogLevel.Warning,
        Message = "Checksum verification failed for file {FileId} using algorithm {Algorithm}. Expected: {Expected}, Calculated: {Calculated}."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void ChecksumVerificationFailedCore(
        this ILogger logger,
        string fileId,
        string algorithm,
        string expected,
        string calculated
    );

    [LoggerMessage(
        EventId = 3233,
        Level = LogLevel.Error,
        Message = "Failed to verify checksum for file {FileId} using algorithm {Algorithm}"
    )]
    public static partial void ChecksumVerificationFailedUnexpectedly(
        this ILogger logger,
        Exception exception,
        string fileId,
        string algorithm
    );

    [LoggerMessage(
        EventId = 3234,
        Level = LogLevel.Error,
        Message = "Pre-calculated checksum missing for file {FileId} - this indicates a bug or corrupted metadata during upload"
    )]
    public static partial void PreCalculatedChecksumMissing(this ILogger logger, string fileId);

    [LoggerMessage(EventId = 3235, Level = LogLevel.Debug, Message = "Using pre-calculated checksum for file {FileId}")]
    public static partial void UsingPreCalculatedChecksum(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3236,
        Level = LogLevel.Error,
        Message = "Invalid stored checksum format for file {FileId} - metadata is corrupted"
    )]
    public static partial void InvalidStoredChecksumFormat(this ILogger logger, Exception exception, string fileId);

    [LoggerMessage(
        EventId = 3245,
        Level = LogLevel.Warning,
        Message = "Checksum algorithm mismatch for file {FileId}: client requested {Requested}, data was staged with {Stored}"
    )]
    public static partial void ChecksumAlgorithmMismatch(
        this ILogger logger,
        string fileId,
        string requested,
        string stored
    );

    [LoggerMessage(
        EventId = 3237,
        Level = LogLevel.Debug,
        Message = "Committed last chunk for file {FileId}: {TotalBlocks} blocks (atomic with metadata update)"
    )]
    public static partial void LastChunkCommitted(this ILogger logger, string fileId, int totalBlocks);

    [LoggerMessage(
        EventId = 3246,
        Level = LogLevel.Warning,
        Message = "Discarding last chunk of file {FileId}: checksum-trailer was faulty or the client disconnected before sending it"
    )]
    public static partial void TrailerFallbackChunkDiscarded(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3247,
        Level = LogLevel.Warning,
        Message = "Rolled back file {FileId} to offset {ChunkStartOffset} after checksum failure"
    )]
    public static partial void LastChunkRolledBack(this ILogger logger, string fileId, long chunkStartOffset);

    [LoggerMessage(
        EventId = 3248,
        Level = LogLevel.Error,
        Message = "No verifiable chunk for file {FileId}: no pre-computed digest and no recorded chunk offset"
    )]
    public static partial void NoVerifiableChunk(this ILogger logger, string fileId);

    [LoggerMessage(
        EventId = 3249,
        Level = LogLevel.Error,
        Message = "Last-chunk state for file {FileId} is corrupted: recorded chunk offset {ChunkStartOffset} does not align with committed content ({CommittedLength})"
    )]
    public static partial void LastChunkStateCorrupted(
        this ILogger logger,
        string fileId,
        long chunkStartOffset,
        long committedLength
    );

    [LoggerMessage(EventId = 3238, Level = LogLevel.Error, Message = "Failed to commit last chunk for file {FileId}")]
    public static partial void CommitLastChunkFailed(this ILogger logger, Exception exception, string fileId);
}
