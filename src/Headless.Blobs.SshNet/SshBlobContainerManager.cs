// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Headless.Blobs.SshNet;

/// <summary>
/// <see cref="IBlobContainerManager"/> implementation for SFTP root directories. Registered as a separately-resolved
/// capability (keyed + default), not a cast from <see cref="SshBlobStorage"/>, mirroring the per-instance isolation of
/// the storage (KTD5).
/// </summary>
/// <remarks>
/// Shares the DI-owned <see cref="SftpClientPool"/> with the storage and never disposes it. The container name is
/// validated for path security and normalized before any directory operation, so <c>EnsureContainerAsync("../x")</c>
/// is rejected at the seam rather than escaping into the directory tree (this is the public-ensure half of the H3
/// fold).
/// </remarks>
internal sealed class SshBlobContainerManager(
    SftpClientPool pool,
    IBlobNamingNormalizer normalizer,
    ILogger<SshBlobContainerManager> logger
) : IBlobContainerManager
{
    public async ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var containerPath = _NormalizeContainer(container);

        logger.LogEnsuringContainer(containerPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // mkdir -p the validated + normalized container path. Segments come from the normalized name, never raw
            // input, so this cannot create a traversal directory.
            var segments = containerPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;

            foreach (var segment in segments)
            {
                current = current.Length == 0 ? segment : $"{current}/{segment}";

                if (await client.ExistsAsync(current, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                logger.LogCreatingContainerSegment(segment);

                try
                {
                    await client.CreateDirectoryAsync(current, cancellationToken).ConfigureAwait(false);
                }
                catch (SshException)
                {
                    // Tolerate a concurrent create; rethrow only if the directory still does not exist.
                    if (!await client.ExistsAsync(current, cancellationToken).ConfigureAwait(false))
                    {
                        throw;
                    }
                }
            }
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default)
    {
        var containerPath = _NormalizeContainer(container);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ISftpFile file;

            try
            {
                file = await client.GetAsync(containerPath, cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }

            return file.IsDirectory;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var containerPath = _NormalizeContainer(container);

        logger.LogDeletingContainer(containerPath);

        var client = await pool.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ISftpFile directory;

            try
            {
                directory = await client.GetAsync(containerPath, cancellationToken).ConfigureAwait(false);
            }
            catch (SftpPathNotFoundException)
            {
                logger.LogDeleteContainerNotFound(containerPath);

                return false;
            }

            if (!directory.IsDirectory)
            {
                return false;
            }

            // SFTP rmdir requires an empty directory, so drain contents (depth-first) before removing the root.
            await _DeleteContentsAsync(client, containerPath, cancellationToken).ConfigureAwait(false);
            await client.DeleteDirectoryAsync(containerPath, cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            await pool.ReleaseAsync(client).ConfigureAwait(false);
        }
    }

    private static async Task _DeleteContentsAsync(
        SftpClient client,
        string directory,
        CancellationToken cancellationToken
    )
    {
        await foreach (var file in client.ListDirectoryAsync(directory, cancellationToken).ConfigureAwait(false))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (file.IsDirectory)
            {
                await _DeleteContentsAsync(client, file.FullName, cancellationToken).ConfigureAwait(false);
                await client.DeleteDirectoryAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await client.DeleteFileAsync(file.FullName, cancellationToken).ConfigureAwait(false);
                }
                catch (SftpPathNotFoundException)
                {
                    // Concurrently removed; nothing to delete.
                }
            }
        }
    }

    private string _NormalizeContainer(string container)
    {
        Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        return normalizer.NormalizeContainerName(container);
    }
}
