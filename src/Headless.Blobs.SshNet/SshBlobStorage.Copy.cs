// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Renci.SshNet.Common;

namespace Headless.Blobs.SshNet;

internal sealed partial class SshBlobStorage
{
    public async ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        var (sourcePath, sourceSidecar) = _ResolvePaths(source);
        var (destPath, destSidecar) = _ResolvePaths(destination);

        if (string.Equals(sourcePath, destPath, StringComparison.Ordinal))
        {
            // A resolved self-copy is a no-op: the blob already holds the content and metadata a copy would write.
            return true;
        }

        logger.LogCopyingBlob(sourcePath, destPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Stream sourceStream;

            // Only the source open can answer "source not found" (the false contract). Destination-side path
            // failures — a never-provisioned container, or a directory removed concurrently — are a different
            // condition and must surface as themselves rather than be reported as a missing source.
            try
            {
                sourceStream = await client
                    .OpenAsync(sourcePath, FileMode.Open, FileAccess.Read, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException ex)
            {
                logger.LogCopySourceNotFound(ex, sourcePath);

                return false;
            }

            await using (sourceStream.ConfigureAwait(false))
            {
                await _ReplaceBlobAsync(
                        client,
                        destPath,
                        destSidecar,
                        async (contentTemp, sidecarTemp, ct) =>
                        {
                            await _EnsureParentDirectoryAsync(client, destPath, ct).ConfigureAwait(false);
                            await _WriteAllAsync(client, contentTemp, sourceStream, ct).ConfigureAwait(false);

                            // Move the sidecar with the blob. Without a source sidecar the replace drops any stale
                            // destination sidecar, so the copy does not inherit the previous occupant's metadata.
                            var sourceMetadata = await _ReadSidecarAsync(client, sourceSidecar, ct)
                                .ConfigureAwait(false);

                            if (sourceMetadata is null)
                            {
                                return false;
                            }

                            await _WriteSidecarAsync(client, sidecarTemp, sourceMetadata, ct).ConfigureAwait(false);

                            return true;
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }
}
