// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Fencing;
using Headless.Fencing.InMemory;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// In-memory leaf fixture for the fencing conformance suite. Every host it configures shares one lease table, the way
/// hosts built by a relational fixture share one database. Units are resource-less, so the scenarios that need a
/// connection, an observed transaction, or a second database do not apply. Tests run serially because the blocking
/// scenarios measure how long a call waits.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class InMemoryFencingFixture : ICollectionFixture<InMemoryFencingFixture>, ILeasesFixture, IDisposable
{
    private readonly InMemoryLeaseStorage _storage = new();
    private readonly ConcurrentQueue<(string Kind, LeaseHandoff Handoff)> _handoffs = new();

    public bool RunsUnitsOnConnections => false;

    public void ConfigureProvider(HeadlessFencingSetupBuilder setup)
    {
        setup.RegisterExtension(new InMemoryFencingOptionsExtension(_storage));
    }

    public DbConnection CreateConnection()
    {
        throw _NoDatabase();
    }

    public DbConnection CreateOtherDatabaseConnection()
    {
        throw _NoDatabase();
    }

    public ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        throw _NoDatabase();
    }

    public IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        throw _NoDatabase();
    }

    public Task<StoredLease?> ReadLeaseAsync(LeaseKey key, CancellationToken cancellationToken)
    {
        var row = _storage.Table.Read(key);

        return Task.FromResult(
            row is null
                ? null
                : new StoredLease(
                    row.Generation,
                    (StoredLeaseState)row.State,
                    row.GrantedAt,
                    row.ExpiresAt,
                    row.EndedAt
                )
        );
    }

    public Task ShiftIntoPastAsync(LeaseKey key, TimeSpan by, CancellationToken cancellationToken)
    {
        var row = _storage.Table.Read(key);
        row.Should().NotBeNull("the lease row to age must exist");

        _storage.Table.Write(
            key,
            row! with
            {
                GrantedAt = row.GrantedAt - by,
                ExpiresAt = row.ExpiresAt - by,
                EndedAt = row.EndedAt - by,
            }
        );

        return Task.CompletedTask;
    }

    public Task WriteHandoffAsync(IUnitOfWork unit, ExpiredLease lease, CancellationToken cancellationToken)
    {
        // The handoff joins the table only when the unit completes, as a row written in the claim's transaction would.
        unit.OnCompleted(() =>
        {
            _handoffs.Enqueue((lease.Kind, new LeaseHandoff(lease.TenantId ?? "", lease.Resource, lease.Generation)));

            return ValueTask.CompletedTask;
        });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LeaseHandoff>> ReadHandoffsAsync(string kind, CancellationToken cancellationToken)
    {
        IReadOnlyList<LeaseHandoff> handoffs =
        [
            .. _handoffs
                .Where(h => string.Equals(h.Kind, kind, StringComparison.Ordinal))
                .Select(static h => h.Handoff),
        ];

        return Task.FromResult(handoffs);
    }

    public void Dispose()
    {
        _storage.Dispose();
    }

    private static NotSupportedException _NoDatabase()
    {
        return new NotSupportedException("The in-memory provider has no database; this scenario does not apply.");
    }
}
