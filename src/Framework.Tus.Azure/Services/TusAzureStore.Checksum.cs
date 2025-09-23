// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusChecksumStore
{
    /*
     * The Client and the Server MAY implement and use this extension (Checksum)
     * to verify data integrity of each PATCH request.
     */

    private readonly Dictionary<string, Func<HashAlgorithm>> _supportedAlgorithms = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
#pragma warning disable CA5350 // Weak cryptographic algorithms
        { "sha1", SHA1.Create },
#pragma warning restore CA5350
        { "sha256", SHA256.Create },
        { "sha512", SHA512.Create },
        { "md5", MD5.Create },
    };

    public Task<IEnumerable<string>> GetSupportedAlgorithmsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<string>>(_supportedAlgorithms.Keys);
    }

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
            var calculatedChecksum = await _CalculateChecksumFromBlobAsync(blobClient, algorithm, cancellationToken);
            var isValid = _VerifyChecksum(calculatedChecksum, checksum);

            if (isValid)
            {
                _logger.LogDebug(
                    "Checksum verification passed for file {FileId} using algorithm {Algorithm}",
                    fileId,
                    algorithm
                );

                return true;
            }

            _logger.LogWarning(
                "Checksum verification failed for file {FileId} using algorithm {Algorithm}",
                fileId,
                algorithm
            );

            // TODO: remove
            // According to TUS spec, we should remove the uploaded data if checksum fails
            // For block blobs, we could potentially remove the last staged blocks, but it's complex
            // For now, we'll just report the failure

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

    private async Task<byte[]> _CalculateChecksumFromBlobAsync(
        BlobClient blobClient,
        string algorithm,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var downloadResponse = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            await using var stream = downloadResponse.Value.Content;

            return await _CalculateChecksumAsync(stream, algorithm, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to calculate checksum for blob {BlobName}", blobClient.Name);

            throw;
        }
    }

    private async Task<byte[]> _CalculateChecksumAsync(
        Stream data,
        string algorithm,
        CancellationToken cancellationToken = default
    )
    {
        if (!_supportedAlgorithms.TryGetValue(algorithm, out var algorithmFactory))
        {
            throw new ArgumentException($"Unsupported checksum algorithm: {algorithm}", nameof(algorithm));
        }

        using var hashAlgorithm = algorithmFactory();

        // Reset stream position if possible
        if (data.CanSeek)
        {
            data.Position = 0;
        }

        var hash = await hashAlgorithm.ComputeHashAsync(data, cancellationToken);

        _logger.LogDebug("Calculated {Algorithm} checksum: {Hash}", algorithm, Convert.ToHexString(hash));

        return hash;
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

        return !calculatedChecksum.Where((t, i) => t != expectedChecksum[i]).Any();
    }
}
