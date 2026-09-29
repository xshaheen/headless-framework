// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Caching;
using Headless.Idempotency;
using Headless.Idempotency.Caching;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Cache leaf fixture for the idempotency conformance suite, over the in-memory <see cref="ICache" />. Every host it
/// configures shares one cache instance, the way replicas share one Redis, and units are resource-less. Tests run
/// serially so no scenario's clock-sensitive steps compete with another's load.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class CacheIdempotencyFixture
    : ICollectionFixture<CacheIdempotencyFixture>,
        IIdempotencyFixture,
        IAsyncDisposable
{
    public const string KeyPrefix = "conformance:idempotency:";

    private readonly ServiceProvider _cacheServices;

    public CacheIdempotencyFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCaching(static setup => setup.UseInMemory());
        _cacheServices = services.BuildServiceProvider();
        Cache = _cacheServices.GetRequiredService<ICache>();
    }

    public ICache Cache { get; }

    public bool RunsUnitsOnConnections => false;

    public void ConfigureIdempotency(HeadlessIdempotencySetupBuilder setup)
    {
        setup.RegisterExtension(
            new CacheIdempotencyOptionsExtension(
                configure: static options => options.KeyPrefix = KeyPrefix,
                sharedCache: Cache
            )
        );
    }

    public DbConnection CreateConnection()
    {
        throw new NotSupportedException("The cache provider has no database; this scenario does not apply.");
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException("The cache provider has no database; this scenario does not apply.");
    }

    public async Task<StoredRecord?> ReadRecordAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        var row = await _ReadAsync(key, cancellationToken);

        return row is null
            ? null
            : new StoredRecord(
                row.Status,
                row.FingerprintAlgorithm,
                row.Fingerprint,
                row.Generation,
                row.LeaseExpiresAt,
                row.Result,
                row.ResultContract,
                row.RetentionUntil,
                row.RecoveryPoint,
                row.RecoveryState,
                row.RecoveryContract
            );
    }

    public async Task ShiftRecordIntoPastAsync(
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        var row = await _ReadAsync(key, cancellationToken);
        row.Should().NotBeNull("the record to age must exist");
        await _WriteAsync(key, row! with { RetentionUntil = row.RetentionUntil - by }, cancellationToken);
    }

    public async Task ShiftLeaseIntoPastAsync(
        IdempotencyRecordKey key,
        TimeSpan by,
        CancellationToken cancellationToken
    )
    {
        var row = await _ReadAsync(key, cancellationToken);
        row?.LeaseExpiresAt.Should().NotBeNull("the lease to age must exist");
        await _WriteAsync(key, row! with { LeaseExpiresAt = row.LeaseExpiresAt - by }, cancellationToken);
    }

    public Task TouchAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        // A resource-less unit has no transaction to begin; the unit is live from the moment it is begun.
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _cacheServices.DisposeAsync();
    }

    private async Task<CacheIdempotencyEntry?> _ReadAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        var cacheKey = CacheIdempotencyRecordStore.RecordKey(KeyPrefix, key);
        var value = await Cache.GetAsync<string>(cacheKey, cancellationToken);

        return value is { HasValue: true, Value: { } raw } ? CacheIdempotencyEntry.Deserialize(cacheKey, raw) : null;
    }

    private async Task _WriteAsync(
        IdempotencyRecordKey key,
        CacheIdempotencyEntry row,
        CancellationToken cancellationToken
    )
    {
        // Rewritten with the entry's remaining cache lifetime: aging a record changes what the store decides from its
        // instants, not how long the cache keeps it.
        var cacheKey = CacheIdempotencyRecordStore.RecordKey(KeyPrefix, key);
        var lifetime = await Cache.GetExpirationAsync(cacheKey, cancellationToken);
        await Cache.UpsertAsync(cacheKey, row.Serialize(), lifetime, cancellationToken);
    }
}
