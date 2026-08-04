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
            // A resolved self-copy is a no-op: opening destPath with FileMode.Create would truncate the source.
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
                await _EnsureParentDirectoryAsync(client, destPath, cancellationToken).ConfigureAwait(false);

                await using var destStream = await client
                    .OpenAsync(destPath, FileMode.Create, FileAccess.Write, cancellationToken)
                    .ConfigureAwait(false);

                await sourceStream.CopyToAsync(destStream, cancellationToken).ConfigureAwait(false);
            }

            // Move the sidecar with the blob. If the source has no sidecar, drop any stale destination sidecar so the
            // copied blob does not inherit the previous occupant's metadata.
            var sourceMetadata = await _ReadSidecarAsync(client, sourceSidecar, cancellationToken)
                .ConfigureAwait(false);

            if (sourceMetadata is not null)
            {
                await _WriteSidecarAsync(client, destSidecar, sourceMetadata, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _DeleteFileIfExistsAsync(client, destSidecar, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }
}
