// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Headless.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

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
     * to verify data integrity of each PATCH request.
     *
     * How append work:
     * Implementation note: Uses tusdotnet's StreamExtensions.GetUploadChecksumInfo() to detect
     * if the client requested checksum verification, allowing conditional block commitment:
     * - if the client requests a checksum verification, don't commit in append and commit here after verification
     * - if the client doesn't request a checksum verification, commit in append and do nothing here
     */

    // This implements the TUS Checksum Extension's PATCH verification flow:
    // 1. Retrieves pre-calculated checksum from metadata (calculated during AppendDataAsync)
    // 2. Performs constant-time comparison to prevent timing attacks
    // 3. On match: Commits staged blocks + updates metadata atomically
    // 4. On mismatch: Leaves blocks uncommitted (Azure auto-deletes after 7 days)

    // Uses "fail fast" approach - if pre-calculated checksum is missing, verification fails
    // without attempting recalculation, indicating a bug or corrupted metadata.

    /// <summary>
    /// Verifies the checksum of the most recent PATCH against the digest supplied by the client
    /// and, on success, atomically commits the staged blocks.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="algorithm">hash algorithm name as reported in the TUS-Checksum header (e.g. <c>sha256</c>)</param>
    /// <param name="checksum">expected digest bytes supplied by the client</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// <see langword="true"/> if the stored pre-calculated digest matches <paramref name="checksum"/>
    /// and the staged blocks were committed; <see langword="false"/> on mismatch, missing metadata,
    /// or unexpected error — staged blocks are left uncommitted and Azure discards them after seven days
    /// </returns>
    /// <remarks>
    /// The digest is not recalculated here; it was computed during <c>AppendDataAsync</c> and
    /// stored in blob metadata under <c>tus_last_chunk_checksum</c>. A missing checksum value
    /// in metadata indicates a bug or corrupted state and is treated as a verification failure.
    /// Comparison uses <c>CryptographicOperations.FixedTimeEquals</c> to prevent timing attacks.
    /// </remarks>
    public async Task<bool> VerifyChecksumAsync(
        string fileId,
        string algorithm,
        byte[] checksum,
        CancellationToken cancellationToken
    )
    {
        var blockBlobClient = _GetBlockBlobClient(fileId);
        TusAzureFile file;
        byte[] calculatedChecksum;

        try
        {
            var blobClient = _GetBlobClient(fileId);

            // Get file info to access stored chunk metadata
            var info = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false);

            if (info is null)
            {
                _logger.ChecksumVerificationFileInfoNotFound(fileId);
                return false;
            }

            file = info;

            // Get pre-calculated checksum from metadata (calculated during upload)
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

            // Don't commit the last chunk, effectively discarding it (since it's not part of the committed blob)
            // as the TUS protocol requires.
            return false;
        }

        _logger.ChecksumVerificationPassed(fileId, algorithm);

        // Commit OUTSIDE the catch-all above: a failure here is an infrastructure error (throttling, network),
        // NOT a checksum mismatch. Let it propagate so the client retries the verification, instead of being
        // told via a false return that its (correctly-checksummed) data was corrupt and discarding it.
        await _CommitLastChunkAsync(blockBlobClient, file, cancellationToken).ConfigureAwait(false);

        return true;
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

            // Build complete block list BEFORE clearing metadata (use LastChunkBlocks while it still has data)
            List<string> allBlockIds = [.. committedBlocks.Select(x => x.Name), .. file.Metadata.LastChunkBlocks ?? []];
            _EnsureWithinBlockLimit(allBlockIds.Count);

            // NOW clear chunk tracking metadata (after we've used it)
            file.Metadata.LastChunkBlocks = null;
            file.Metadata.LastChunkChecksum = null;

            // ATOMIC: Commit blocks + update metadata in single operation
            var options = new CommitBlockListOptions { Metadata = file.Metadata.ToAzure() };
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

    [LoggerMessage(EventId = 3238, Level = LogLevel.Error, Message = "Failed to commit last chunk for file {FileId}")]
    public static partial void CommitLastChunkFailed(this ILogger logger, Exception exception, string fileId);
}
