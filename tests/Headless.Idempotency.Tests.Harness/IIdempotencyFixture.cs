// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// What a provider leaf supplies to run <see cref="IdempotencyConformanceTests{TFixture}" />: fencing and idempotency
/// registrations against one database, raw connections to it, the provider's raw-ADO unit entry point, and direct
/// access to the stored record and lease rows.
/// </summary>
public interface IIdempotencyFixture
{
    /// <summary>Chooses the fencing provider under test, pointed at the shared database.</summary>
    void ConfigureFencing(HeadlessFencingSetupBuilder setup);

    /// <summary>Chooses the idempotency provider under test, pointed at the same database.</summary>
    void ConfigureIdempotency(HeadlessIdempotencySetupBuilder setup);

    /// <summary>Creates an unopened connection to the shared database.</summary>
    DbConnection CreateConnection();

    /// <summary>Begins an owned unit on <paramref name="connection" /> through the provider's raw-ADO entry point.</summary>
    ValueTask<IUnitOfWork> BeginOwnedAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads the committed record of <paramref name="key" /> on an independent connection, or <see langword="null" />
    /// when it has no row. The suite calls it only while no unit holds the row.
    /// </summary>
    Task<StoredRecord?> ReadRecordAsync(IdempotencyRecordKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the record's <c>retention_until</c> back by <paramref name="by" /> on an independent connection, so a test
    /// can let time pass without waiting.
    /// </summary>
    Task ShiftRecordIntoPastAsync(IdempotencyRecordKey key, TimeSpan by, CancellationToken cancellationToken);

    /// <summary>Reads the committed idempotency lease of <paramref name="key" />, or <see langword="null" />.</summary>
    Task<StoredLeaseRow?> ReadLeaseAsync(IdempotencyRecordKey key, CancellationToken cancellationToken);

    /// <summary>Moves every stored instant of the key's idempotency lease back by <paramref name="by" />.</summary>
    Task ShiftLeaseIntoPastAsync(IdempotencyRecordKey key, TimeSpan by, CancellationToken cancellationToken);
}

/// <summary>One stored idempotency record row.</summary>
public sealed record StoredRecord(
    IdempotencyRecordStatus Status,
    string FingerprintAlgorithm,
    byte[] Fingerprint,
    long? LeaseGeneration,
    byte[]? Result,
    string? ResultContract,
    DateTimeOffset RetentionUntil
);

/// <summary>The stored state of an idempotency lease, as the fencing table holds it.</summary>
public enum StoredLeaseRowState
{
    Active = 0,
    Settled = 1,
    Released = 2,
    Abandoned = 3,
}

/// <summary>One stored idempotency lease row.</summary>
public sealed record StoredLeaseRow(long Generation, StoredLeaseRowState State, DateTimeOffset ExpiresAt);

public static class IdempotencyFixtureExtensions
{
    /// <summary>
    /// How long a call waiting on another transaction's record row must stay blocked. Long enough that a call not
    /// waiting on the row lock would certainly have returned within it.
    /// </summary>
    public static readonly TimeSpan BlockedObservationWindow = TimeSpan.FromMilliseconds(750);

    /// <summary>Upper bound for a blocked call to finish once the holder ends its transaction.</summary>
    public static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Builds an independent host (its own service provider and connection pool) with fencing and idempotency on the
    /// provider under test, runs their storage initializers, and returns it. The retention purge is off unless
    /// <paramref name="configure" /> turns it on; the hosted service is never started by this method.
    /// </summary>
    public static async ValueTask<IdempotencyHost> CreateHostAsync(
        this IIdempotencyFixture fixture,
        Action<HeadlessIdempotencySetupBuilder>? configure = null,
        CancellationToken cancellationToken = default
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessFencing(fixture.ConfigureFencing);
        services.AddHeadlessIdempotency(setup =>
        {
            fixture.ConfigureIdempotency(setup);
            setup.ConfigureOptions(options => options.PurgeInterval = null);
            configure?.Invoke(setup);
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new IdempotencyHost(provider);
    }

    /// <summary>Begins an owned unit on a new connection to the shared database.</summary>
    public static async ValueTask<IdempotencyUnit> BeginUnitAsync(
        this IIdempotencyFixture fixture,
        IdempotencyHost host,
        CancellationToken cancellationToken = default
    )
    {
        var connection = fixture.CreateConnection();

        try
        {
            var unit = await fixture.BeginOwnedAsync(host.Factory, connection, cancellationToken).ConfigureAwait(false);

            return new IdempotencyUnit(unit, connection);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }
}

/// <summary>One independently built host: its own service provider, options, and connection pool.</summary>
public sealed class IdempotencyHost(ServiceProvider services) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public IIdempotentOperations Operations { get; } = services.GetRequiredService<IIdempotentOperations>();

    public IIdempotencyRecordStore Store { get; } = services.GetRequiredService<IIdempotencyRecordStore>();

    public IFencedLeases Leases { get; } = services.GetRequiredService<IFencedLeases>();

    public IUnitOfWorkFactory Factory { get; } = services.GetRequiredService<IUnitOfWorkFactory>();

    public ICurrentTenant CurrentTenant { get; } = services.GetRequiredService<ICurrentTenant>();

    /// <summary>The registered retention purge service, not started.</summary>
    public IHostedService RetentionService { get; } =
        services
            .GetServices<IHostedService>()
            .Single(static s =>
                string.Equals(s.GetType().Name, "IdempotencyRetentionService", StringComparison.Ordinal)
            );

    public ValueTask DisposeAsync()
    {
        return services.DisposeAsync();
    }
}

/// <summary>An owned unit of work over a raw connection, disposing the connection with it.</summary>
public sealed class IdempotencyUnit(IUnitOfWork unit, DbConnection connection) : IAsyncDisposable
{
    public IUnitOfWork Unit { get; } = unit;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Unit.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RollbackAsync()
    {
        await Unit.RollbackAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Unit.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }
}
