// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Checks;
using StackExchange.Redis;

namespace Headless.Blobs.Redis;

/// <summary>
/// <see cref="IBlobContainerManager"/> implementation for the Redis blob backend. Registered as a separately-resolved
/// capability (keyed + default), mirroring the per-instance isolation of <see cref="RedisBlobStorage"/>.
/// </summary>
/// <remarks>
/// Redis has no first-class container concept: each container maps to two hashes — a content hash (<c>{container}/</c>)
/// and an info hash (<c>blob-info/{container}/</c>) — both created lazily on the first <c>HSET</c>. Because a write to a
/// "missing" container always succeeds, <see cref="EnsureContainerAsync"/> is an honest no-op. A container is considered
/// to exist when either backing hash holds at least one field, and deleting a container drops both hashes.
/// </remarks>
internal sealed class RedisBlobContainerManager(
    IConnectionMultiplexer connectionMultiplexer,
    IBlobNamingNormalizer normalizer
) : IBlobContainerManager
{
    private IDatabase Database => connectionMultiplexer.GetDatabase();

    public ValueTask EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        // Honest no-op: the backing hash is created on the first write, so an upload to a "missing" container always
        // succeeds and there is nothing to provision. The name is still validated so a bad container fails the same
        // way it would on the data plane.
        _ValidateContainer(container);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }

    public async ValueTask<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (blobsHash, infoHash) = _BuildHashKeys(container);

        // Each container is exactly one Redis hash key, and an empty hash cannot exist in Redis, so key-existence is
        // the exact, O(1), cluster-safe equivalent of "the container holds at least one blob" — strictly cleaner than
        // a partial HSCAN COUNT 1, which risks a false negative when the first scan page of a large hash is empty.
        if (await Database.KeyExistsAsync(blobsHash).ConfigureAwait(false))
        {
            return true;
        }

        return await Database.KeyExistsAsync(infoHash).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (blobsHash, infoHash) = _BuildHashKeys(container);

        // Drop both backing hashes. Two single-key deletes (rather than a multi-key one) keep this cluster-safe: the
        // content and info hashes live in different key slots. KeyDelete returns true only when the key existed.
        var blobsDeleted = await Database.KeyDeleteAsync(blobsHash).ConfigureAwait(false);
        var infoDeleted = await Database.KeyDeleteAsync(infoHash).ConfigureAwait(false);

        return blobsDeleted || infoDeleted;
    }

    private (string BlobsHash, string InfoHash) _BuildHashKeys(string container)
    {
        var normalized = _ValidateContainer(container);
        var blobsHash = normalized.EnsureEndsWith('/');
        var infoHash = ("blob-info/" + normalized).EnsureEndsWith('/');

        return (blobsHash, infoHash);
    }

    private string _ValidateContainer(string container)
    {
        return BlobLocationResolver.ResolveContainer(container, normalizer);
    }
}
