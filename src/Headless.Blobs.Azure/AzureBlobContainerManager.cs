// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Threading;

namespace Headless.Blobs.Azure;

/// <summary>
/// <see cref="IBlobContainerManager"/> implementation for Azure Blob Storage containers. Registered as a separate
/// keyed/default service (per-instance, mirroring the storage) rather than implemented by <see cref="AzureBlobStorage"/>
/// itself, so container management is discoverable from DI and segregated from the data plane (KTD5).
/// </summary>
/// <remarks>
/// The <see cref="BlobServiceClient"/> is owned by DI / the caller's client factory — the same client the storage
/// engine uses — so this manager never disposes it; it only disposes its own per-container ensure lock.
/// </remarks>
internal sealed class AzureBlobContainerManager : IBlobContainerManager, IDisposable
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IBlobNamingNormalizer _normalizer;
    private readonly PublicAccessType _publicAccessType;

    // Containers this instance has already ensured exist, so CreateIfNotExists runs at most once per container. A
    // container is recorded only after a successful create, so a failed ensure is naturally retried (L5). The
    // per-container lock serializes concurrent first-time ensures of the same container while letting distinct
    // containers run in parallel, and DeleteContainerAsync takes the same lock so an ensure never races a delete of
    // the same container.
    private readonly ConcurrentDictionary<string, byte> _ensuredContainers = new(StringComparer.Ordinal);
    private readonly KeyedAsyncLock _ensureContainerLock = new();

    public AzureBlobContainerManager(
        BlobServiceClient blobServiceClient,
        IBlobNamingNormalizer normalizer,
        PublicAccessType publicAccessType
    )
    {
        _blobServiceClient = Argument.IsNotNull(blobServiceClient);
        _normalizer = Argument.IsNotNull(normalizer);
        _publicAccessType = publicAccessType;
    }

    public async ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var name = _NormalizeContainer(container);

        await _EnsureContainerOnceAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default)
    {
        var name = _NormalizeContainer(container);

        var response = await _blobServiceClient
            .GetBlobContainerClient(name)
            .ExistsAsync(cancellationToken)
            .ConfigureAwait(false);

        return response.Value;
    }

    public async ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        var name = _NormalizeContainer(container);

        // Serialize with the ensure path's per-container lock and evict the ensured-cache entry BEFORE touching the
        // backend. Evicting only after the backend delete leaves a window where a concurrent ensure fast-path sees
        // the stale entry and reports success for a container mid-deletion; holding the lock makes a concurrent
        // first-time ensure wait and re-create the container only after this delete completes.
        using (await _ensureContainerLock.LockAsync(name, cancellationToken).ConfigureAwait(false))
        {
            _ensuredContainers.TryRemove(name, out _);

            var response = await _blobServiceClient
                .GetBlobContainerClient(name)
                .DeleteIfExistsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Value;
        }
    }

    private async Task _EnsureContainerOnceAsync(string container, CancellationToken cancellationToken)
    {
        if (_ensuredContainers.ContainsKey(container))
        {
            return;
        }

        using (await _ensureContainerLock.LockAsync(container, cancellationToken).ConfigureAwait(false))
        {
            // Re-check under the lock: a concurrent caller may have ensured this container while we waited.
            if (_ensuredContainers.ContainsKey(container))
            {
                return;
            }

            await _blobServiceClient
                .GetBlobContainerClient(container)
                .CreateIfNotExistsAsync(_publicAccessType, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Record only after a successful create so a failed ensure is retried next time.
            _ensuredContainers.TryAdd(container, 0);
        }
    }

    private string _NormalizeContainer(string container)
    {
        Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        var normalized = _normalizer.NormalizeContainerName(container);

        if (string.IsNullOrWhiteSpace(normalized) || normalized is "." or "..")
        {
            throw new ArgumentException(
                "The blob container resolves to the storage root after provider normalization.",
                nameof(container)
            );
        }

        PathValidation.ValidatePathSegment(normalized);

        return normalized;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Only the ensure lock is owned here; the BlobServiceClient is owned by DI / the caller's client factory.
        _ensureContainerLock.Dispose();
    }
}
