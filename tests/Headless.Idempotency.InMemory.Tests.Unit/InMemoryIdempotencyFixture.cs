// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Idempotency;
using Headless.Idempotency.InMemory;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// In-memory leaf fixture for the idempotency conformance suite. Every host it configures shares one record table,
/// the way hosts built by a relational fixture share one database, and units are resource-less. Tests run serially
/// because the blocking scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class InMemoryIdempotencyFixture
    : ICollectionFixture<InMemoryIdempotencyFixture>,
        IIdempotencyFixture,
        IDisposable
{
    private readonly InMemoryIdempotencyStorage _storage = new();

    public bool RunsUnitsOnConnections => false;

    public void ConfigureIdempotency(HeadlessIdempotencySetupBuilder setup)
    {
        setup.RegisterExtension(new InMemoryIdempotencyOptionsExtension(_storage));
    }

    public DbConnection CreateConnection()
    {
        throw new NotSupportedException("The in-memory provider has no database; this scenario does not apply.");
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException("The in-memory provider has no database; this scenario does not apply.");
    }

    public Task<StoredRecord?> ReadRecordAsync(IdempotencyRecordKey key, CancellationToken cancellationToken)
    {
        var row = _storage.Table.Read(key);

        return Task.FromResult(
            row is null
                ? null
                : new StoredRecord(
                    row.Status,
                    row.Fingerprint.Algorithm,
                    row.Fingerprint.Hash.ToArray(),
                    row.Generation,
                    row.LeaseExpiresAt,
                    row.Result?.Payload.ToArray(),
                    row.Result?.Contract,
                    row.RetentionUntil
                )
        );
    }

    public Task ShiftRecordIntoPastAsync(IdempotencyRecordKey key, TimeSpan by, CancellationToken cancellationToken)
    {
        var row = _storage.Table.Read(key);
        row.Should().NotBeNull("the record to age must exist");
        _storage.Table.Write(key, row! with { RetentionUntil = row.RetentionUntil - by });

        return Task.CompletedTask;
    }

    public Task ShiftLeaseIntoPastAsync(IdempotencyRecordKey key, TimeSpan by, CancellationToken cancellationToken)
    {
        var row = _storage.Table.Read(key);
        row?.LeaseExpiresAt.Should().NotBeNull("the lease to age must exist");
        _storage.Table.Write(key, row! with { LeaseExpiresAt = row.LeaseExpiresAt - by });

        return Task.CompletedTask;
    }

    public Task TouchAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        // A resource-less unit has no transaction to begin; the unit is live from the moment it is begun.
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _storage.Dispose();
    }
}
