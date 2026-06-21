// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// Single shared <see cref="NpgsqlDataSource"/> owner for the Postgres distributed-lock consumers
/// (storage, release signal, fencing). Registering one of these as a DI singleton guarantees a
/// connection-string configuration produces a single pool instead of one per consumer.
/// </summary>
/// <remarks>
/// Disposal ownership is centralized here: when the data source was built from a connection string it
/// is owned and disposed on container teardown; when a <see cref="PostgresDistributedLockOptions.DataSource"/>
/// is injected it is the consumer's object and is never disposed. Consumers inject
/// <see cref="DataSource"/> directly and must not dispose it.
/// </remarks>
internal sealed class PostgresLockDataSource : IAsyncDisposable, IDisposable
{
    private readonly bool _owned;

    /// <summary>
    /// Initializes the lock data source, adopting an injected <see cref="NpgsqlDataSource"/> (not owned) or
    /// building one from the configured connection string (owned and disposed on teardown).
    /// </summary>
    /// <param name="options">
    /// Resolved options. When <see cref="PostgresDistributedLockOptions.DataSource"/> is set, it is
    /// used directly and is not owned; otherwise a new <see cref="NpgsqlDataSource"/> is built from
    /// <see cref="PostgresDistributedLockOptions.ConnectionString"/> and is owned.
    /// </param>
    public PostgresLockDataSource(IOptions<PostgresDistributedLockOptions> options)
    {
        DataSource = PostgresDataSourceFactory.CreateDataSource(options.Value);
        _owned = options.Value.DataSource is null;
    }

    /// <summary>
    /// Gets the shared <see cref="NpgsqlDataSource"/> consumed by the storage, release-signal, and
    /// fencing-token services. Consumers must not dispose this instance.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    /// Disposes the owned <see cref="NpgsqlDataSource"/> asynchronously. A no-op when the data source
    /// was injected by the caller and is not owned.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return _owned ? DataSource.DisposeAsync() : default;
    }

    /// <summary>
    /// Disposes the owned <see cref="NpgsqlDataSource"/> synchronously. A no-op when the data source
    /// was injected by the caller and is not owned.
    /// </summary>
    public void Dispose()
    {
        if (_owned)
        {
            DataSource.Dispose();
        }
    }
}
