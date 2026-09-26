// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Idempotency;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// What a provider leaf supplies to run <see cref="IdempotencyConformanceTests{TFixture}" />: the idempotency
/// registration against one database, raw connections to it, the provider's raw-ADO unit entry point, and direct
/// access to the stored record rows.
/// </summary>
public interface IIdempotencyFixture
{
    /// <summary>Chooses the idempotency provider under test, pointed at the shared database.</summary>
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

    /// <summary>
    /// Moves the record's <c>lease_expires_at</c> back by <paramref name="by" /> on an independent connection, so a test
    /// can let an admitted attempt's lease expire without waiting.
    /// </summary>
    Task ShiftLeaseIntoPastAsync(IdempotencyRecordKey key, TimeSpan by, CancellationToken cancellationToken);

    /// <summary>Runs a trivial statement inside <paramref name="unit" />'s transaction, so the transaction has begun.</summary>
    Task TouchAsync(IUnitOfWork unit, CancellationToken cancellationToken);
}

/// <summary>One stored idempotency record row.</summary>
public sealed record StoredRecord(
    IdempotencyRecordStatus Status,
    string FingerprintAlgorithm,
    byte[] Fingerprint,
    long? Generation,
    DateTimeOffset? LeaseExpiresAt,
    byte[]? Result,
    string? ResultContract,
    DateTimeOffset RetentionUntil
);

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
    /// Builds an independent host (its own service provider and connection pool) with idempotency on the provider
    /// under test, runs its storage initializer, and returns it. The retention purge is off unless
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
