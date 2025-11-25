// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Tus.Models;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusChecksumStore
{
    private readonly IEnumerable<string> _cachedSupportedAlgorithms = ["sha1", "sha256", "sha512", "md5"];

    private readonly Dictionary<string, Func<HashAlgorithm>> _supportedAlgorithms = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
#pragma warning disable CA5350 // Weak cryptographic algorithms
        { "sha1", SHA1.Create },
#pragma warning restore CA5350
        { "sha256", SHA256.Create },
        { "sha512", SHA512.Create },
#pragma warning disable CA5351 // Broken cryptographic algorithms
        { "md5", MD5.Create },
#pragma warning restore CA5351
    };

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

    public async Task<bool> VerifyChecksumAsync(
        string fileId,
        string algorithm,
        byte[] checksum,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var blobClient = _GetBlobClient(fileId);
            var blockBlobClient = _GetBlockBlobClient(fileId);

            // Get file info to access stored chunk metadata
            var file = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken);

            if (file is null)
            {
                _logger.LogError("File info not found for {FileId} during checksum verification", fileId);
                return false;
            }

            // Get pre-calculated checksum from metadata (calculated during upload)
            if (!_TryGetChecksum(file, out var calculatedChecksum))
            {
                return false;
            }

            if (_VerifyChecksum(calculatedChecksum, checksum))
            {
                _logger.LogDebug(
                    "Checksum verification passed for file {FileId} using algorithm {Algorithm}",
                    fileId,
                    algorithm
                );

                // Commit staged blocks and update metadata atomically
                await _CommitLastChunkAsync(blockBlobClient, file, cancellationToken);

                return true;
            }

            _logger.LogWarning(
                "Checksum verification failed for file {FileId} using algorithm {Algorithm}. Expected: {Expected}, Calculated: {Calculated}.",
                fileId,
                algorithm,
                Convert.ToHexString(checksum),
                Convert.ToHexString(calculatedChecksum)
            );

            // Don't commit the last chunk, effectively discarding it (since it's not part of the committed blob)
            // as the TUS protocol requires

            return false;
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Failed to verify checksum for file {FileId} using algorithm {Algorithm}",
                fileId,
                algorithm
            );

            return false;
        }
    }

    private HashAlgorithm? _CreateHashAlgorithm(string algorithm)
    {
        return _supportedAlgorithms.TryGetValue(algorithm, out var factory) ? factory() : null;
    }

    private bool _TryGetChecksum(TusAzureFile file, [NotNullWhen(true)] out byte[]? checksum)
    {
        // The checksum is base64-encoded in metadata (LastChunkChecksum) and calculated during
        // AppendDataAsync. If missing, it indicates either:
        // - A bug in the upload flow (checksum should have been calculated)
        // - Corrupted metadata

        var lastChunkChecksum = file.Metadata.LastChunkChecksum;

        if (string.IsNullOrEmpty(lastChunkChecksum))
        {
            _logger.LogError(
                "Pre-calculated checksum missing for file {FileId} - this indicates a bug or corrupted metadata during upload",
                file.FileId
            );

            checksum = null;

            return false;
        }

        try
        {
            checksum = Convert.FromBase64String(lastChunkChecksum);

            _logger.LogDebug("Using pre-calculated checksum for file {FileId}", file.FileId);
        }
        catch (FormatException e)
        {
            _logger.LogError(
                e,
                "Invalid stored checksum format for file {FileId} - metadata is corrupted",
                file.FileId
            );

            checksum = null;

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
            var committedBlocks = await _GetCommittedBlocksAsync(client, token);

            // Build complete block list BEFORE clearing metadata (use LastChunkBlocks while it still has data)
            List<string> allBlockIds = [.. committedBlocks.Select(x => x.Name), .. file.Metadata.LastChunkBlocks ?? []];

            // NOW clear chunk tracking metadata (after we've used it)
            file.Metadata.LastChunkBlocks = null;
            file.Metadata.LastChunkChecksum = null;

            // ATOMIC: Commit blocks + update metadata in single operation
            var options = new CommitBlockListOptions { Metadata = file.Metadata.ToAzure() };
            await client.CommitBlockListAsync(allBlockIds, options, cancellationToken: token);

            _logger.LogDebug(
                "Committed last chunk for file {FileId}: {TotalBlocks} blocks (atomic with metadata update)",
                file.FileId,
                allBlockIds.Count
            );
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to commit last chunk for file {FileId}", file.FileId);

            throw;
        }
    }
}
