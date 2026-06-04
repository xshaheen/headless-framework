// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

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

    public PostgresLockDataSource(IOptions<PostgresDistributedLockOptions> options)
    {
        DataSource = PostgresDataSourceFactory.CreateDataSource(options.Value);
        _owned = options.Value.DataSource is null;
    }

    public NpgsqlDataSource DataSource { get; }

    public ValueTask DisposeAsync()
    {
        return _owned ? DataSource.DisposeAsync() : default;
    }

    public void Dispose()
    {
        if (_owned)
        {
            DataSource.Dispose();
        }
    }
}
