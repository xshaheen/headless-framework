// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.MultiTenancy;
using Headless.Sequences;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// What a provider leaf supplies to run <see cref="SequencesConformanceTests{TFixture}" />: its provider and
/// unit-of-work registration, raw connections to the counter database and to a second database on the same server,
/// the provider's raw-ADO unit entry points, and a direct read of a stored counter.
/// </summary>
public interface ISequencesFixture
{
    /// <summary>Registers the provider's unit-of-work services (for example <c>AddPostgreSqlUnitOfWork</c>).</summary>
    void ConfigureUnitOfWork(IServiceCollection services);

    /// <summary>Chooses the provider under test on the sequences builder, pointed at the counter database.</summary>
    void ConfigureProvider(HeadlessSequencesSetupBuilder setup);

    /// <summary>Creates an unopened connection to the database that holds the counters.</summary>
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
    /// Reads the committed value of <paramref name="key" /> from the default table on an independent connection, or
    /// <see langword="null" /> when the key has no row. The suite calls it only while no unit holds the row.
    /// </summary>
    Task<long?> ReadValueAsync(SequenceKey key, CancellationToken cancellationToken);
}

public static class SequencesFixtureExtensions
{
    /// <summary>
    /// How long a unit waiting on another unit's counter row must stay blocked. Long enough that a call running
    /// autonomously (not waiting on the row lock) would certainly have returned within it.
    /// </summary>
    public static readonly TimeSpan BlockedObservationWindow = TimeSpan.FromMilliseconds(750);

    /// <summary>Upper bound for a blocked call to finish once the holder ends its transaction.</summary>
    public static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Builds an independent host (its own service provider and connection pool) with the provider under test,
    /// runs its storage initializer, and returns it.
    /// </summary>
    public static async ValueTask<SequencesHost> CreateHostAsync(
        this ISequencesFixture fixture,
        Action<HeadlessSequencesSetupBuilder>? configure = null,
        CancellationToken cancellationToken = default
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        fixture.ConfigureUnitOfWork(services);
        services.AddHeadlessSequences(setup =>
        {
            fixture.ConfigureProvider(setup);
            configure?.Invoke(setup);
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SequencesHost(provider);
    }

    /// <summary>Begins an owned unit on a new connection to the counter database.</summary>
    public static ValueTask<SequencesUnit> BeginUnitAsync(
        this ISequencesFixture fixture,
        SequencesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _BeginOwnedAsync(fixture, host, fixture.CreateConnection(), cancellationToken);
    }

    /// <summary>Begins an owned unit on a new connection to the other database.</summary>
    public static ValueTask<SequencesUnit> BeginUnitOnOtherDatabaseAsync(
        this ISequencesFixture fixture,
        SequencesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _BeginOwnedAsync(fixture, host, fixture.CreateOtherDatabaseConnection(), cancellationToken);
    }

    /// <summary>
    /// Opens a transaction the test owns and enlists it in observed mode: the test commits it, the unit only
    /// observes the outcome.
    /// </summary>
    public static ValueTask<SequencesUnit> EnlistUnitAsync(
        this ISequencesFixture fixture,
        SequencesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _EnlistAsync(fixture, host, fixture.CreateConnection(), cancellationToken);
    }

    /// <summary>Enlists an observed-mode unit on a transaction the test owns on the other database.</summary>
    public static ValueTask<SequencesUnit> EnlistUnitOnOtherDatabaseAsync(
        this ISequencesFixture fixture,
        SequencesHost host,
        CancellationToken cancellationToken = default
    )
    {
        return _EnlistAsync(fixture, host, fixture.CreateOtherDatabaseConnection(), cancellationToken);
    }

    private static async ValueTask<SequencesUnit> _BeginOwnedAsync(
        ISequencesFixture fixture,
        SequencesHost host,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var unit = await fixture.BeginOwnedAsync(host.Factory, connection, cancellationToken).ConfigureAwait(false);

            return new SequencesUnit(unit, connection, transaction: null);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private static async ValueTask<SequencesUnit> _EnlistAsync(
        ISequencesFixture fixture,
        SequencesHost host,
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

            return new SequencesUnit(unit, connection, transaction);
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
public sealed class SequencesHost(ServiceProvider services) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public ISequenceGenerator Generator { get; } = services.GetRequiredService<ISequenceGenerator>();

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
public sealed class SequencesUnit : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;

    internal SequencesUnit(IUnitOfWork unit, DbConnection connection, DbTransaction? transaction)
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
