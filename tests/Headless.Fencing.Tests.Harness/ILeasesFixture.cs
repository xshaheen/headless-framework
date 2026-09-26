// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Fencing;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// What a provider leaf supplies to run <see cref="LeasesConformanceTests{TFixture}" />: its provider registration,
/// raw connections to the lease database and to a second database on the same server, the provider's raw-ADO unit
/// entry points, and direct access to the stored lease rows and to a handoff table a sweep handler writes into.
/// </summary>
public interface ILeasesFixture
{
    /// <summary>Chooses the provider under test on the fencing builder, pointed at the lease database.</summary>
    void ConfigureProvider(HeadlessFencingSetupBuilder setup);

    /// <summary>Creates an unopened connection to the database that holds the leases.</summary>
    DbConnection CreateConnection();

    /// <summary>Creates an unopened connection to a different database on the same server.</summary>
    DbConnection CreateOtherDatabaseConnection();

    /// <summary>Begins an owned unit on <paramref name="connection" /> through the provider's raw-ADO entry point.</summary>
    ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    );

    /// <summary>Enlists an open transaction in observed mode through the provider's raw-ADO entry point.</summary>
    IUnitOfWork Enlist(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction);

    /// <summary>
    /// Reads the committed row of <paramref name="key" /> from the default schema on an independent connection, or
    /// <see langword="null" /> when the lease has no row. The suite calls it only while no unit holds the row.
    /// </summary>
    Task<StoredLease?> ReadLeaseAsync(LeaseKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every stored instant of <paramref name="key" /> (granted, expiry, and end) back by <paramref name="by" />
    /// on an independent connection, so a test can age a lease without waiting for the database clock.
    /// </summary>
    Task ShiftIntoPastAsync(LeaseKey key, TimeSpan by, CancellationToken cancellationToken);

    /// <summary>
    /// Records <paramref name="lease" /> in the handoff table through <paramref name="unit" />'s own connection and
    /// transaction, as a sweep handler routing the lease would, so the row commits or rolls back with the claim.
    /// </summary>
    Task WriteHandoffAsync(IUnitOfWork unit, ExpiredLease lease, CancellationToken cancellationToken);

    /// <summary>Reads every committed handoff of <paramref name="kind" /> on an independent connection.</summary>
    Task<IReadOnlyList<LeaseHandoff>> ReadHandoffsAsync(string kind, CancellationToken cancellationToken);
}

/// <summary>A stored lease state, as the provider's table holds it.</summary>
public enum StoredLeaseState
{
    Active = 0,
    Settled = 1,
    Released = 2,
    Abandoned = 3,
}

/// <summary>One stored lease row.</summary>
public sealed record StoredLease(
    long Generation,
    StoredLeaseState State,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? EndedAt
);

/// <summary>One row a sweep handler wrote into the handoff table; the tenant is stored empty for the host scope.</summary>
public sealed record LeaseHandoff(string TenantId, string Resource, long Generation);

/// <summary>An application clock offset from the real one, to prove lease decisions never read it.</summary>
public sealed class SkewedTimeProvider(TimeSpan skew) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return System.GetUtcNow() + skew;
    }
}

public static class LeasesFixtureExtensions
{
    /// <summary>
    /// How long a call waiting on another transaction's lease row must stay blocked. Long enough that a call not
    /// waiting on the row lock would certainly have returned within it.
    /// </summary>
    public static readonly TimeSpan BlockedObservationWindow = TimeSpan.FromMilliseconds(750);

    /// <summary>Upper bound for a blocked call to finish once the holder ends its transaction.</summary>
    public static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Builds an independent host (its own service provider and connection pool) with the provider under test,
    /// runs its storage initializer, and returns it.
    /// </summary>
    public static async ValueTask<LeasesHost> CreateHostAsync(
        this ILeasesFixture fixture,
        Action<HeadlessFencingSetupBuilder>? configure = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        services.AddHeadlessFencing(setup =>
        {
            fixture.ConfigureProvider(setup);
            configure?.Invoke(setup);
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new LeasesHost(provider);
    }

    /// <summary>Begins an owned unit on a new connection to the lease database.</summary>
    public static ValueTask<LeasesUnit> BeginUnitAsync(
        this ILeasesFixture fixture,
        LeasesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _BeginOwnedAsync(fixture, host, fixture.CreateConnection(), cancellationToken);
    }

    /// <summary>Begins an owned unit on a new connection to the other database.</summary>
    public static ValueTask<LeasesUnit> BeginUnitOnOtherDatabaseAsync(
        this ILeasesFixture fixture,
        LeasesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _BeginOwnedAsync(fixture, host, fixture.CreateOtherDatabaseConnection(), cancellationToken);
    }

    /// <summary>
    /// Opens a transaction the test owns and enlists it in observed mode: the test commits it, the unit only
    /// observes the outcome.
    /// </summary>
    public static ValueTask<LeasesUnit> EnlistUnitAsync(
        this ILeasesFixture fixture,
        LeasesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _EnlistAsync(fixture, host, fixture.CreateConnection(), cancellationToken);
    }

    /// <summary>Enlists an observed-mode unit on a transaction the test owns on the other database.</summary>
    public static ValueTask<LeasesUnit> EnlistUnitOnOtherDatabaseAsync(
        this ILeasesFixture fixture,
        LeasesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _EnlistAsync(fixture, host, fixture.CreateOtherDatabaseConnection(), cancellationToken);
    }

    private static async ValueTask<LeasesUnit> _BeginOwnedAsync(
        ILeasesFixture fixture,
        LeasesHost host,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var unit = await fixture.BeginOwnedAsync(host.Factory, connection, cancellationToken).ConfigureAwait(false);

            return new LeasesUnit(unit, connection, transaction: null);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private static async ValueTask<LeasesUnit> _EnlistAsync(
        ILeasesFixture fixture,
        LeasesHost host,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        DbTransaction? transaction = null;

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            transaction = await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
            var unit = fixture.Enlist(host.Factory, connection, transaction);

            return new LeasesUnit(unit, connection, transaction);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }
}

/// <summary>One independently built host: its own service provider, options, and connection pool.</summary>
public sealed class LeasesHost(ServiceProvider services) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public IFencedLeases Leases { get; } = services.GetRequiredService<IFencedLeases>();

    public IUnitOfWorkFactory Factory { get; } = services.GetRequiredService<IUnitOfWorkFactory>();

    public ICurrentTenant CurrentTenant { get; } = services.GetRequiredService<ICurrentTenant>();

    public ValueTask DisposeAsync()
    {
        return services.DisposeAsync();
    }
}

/// <summary>
/// A unit of work over a raw connection, with commit and rollback that honor its mode: an owned unit commits
/// itself, while an observed unit's transaction is committed by the test before the unit is completed.
/// </summary>
public sealed class LeasesUnit : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;

    internal LeasesUnit(IUnitOfWork unit, DbConnection connection, DbTransaction? transaction)
    {
        Unit = unit;
        _connection = connection;
        _transaction = transaction;
    }

    public IUnitOfWork Unit { get; }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await Unit.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RollbackAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await Unit.RollbackAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Unit.DisposeAsync().ConfigureAwait(false);

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
