// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// Single connection source for the Postgres distributed-lock consumers (storage, release signal, schema runner), so
/// they all draw from one pool instead of one each.
/// </summary>
/// <remarks>
/// Three sources, picked from the resolved options:
/// <list type="bullet">
/// <item>An injected <see cref="PostgreSqlDistributedLockOptions.DataSource"/>: the consumer's object, never
/// disposed here.</item>
/// <item>The shared <c>AddPostgreSqlSql</c> connection string (the parameterless <c>UsePostgreSql()</c>): plain
/// <see cref="NpgsqlConnection"/>s on Npgsql's pool for that string, which every other feature on the same string
/// already uses, so locks add no second pool to the database. The string is used as-is; a KeepAlive set on it
/// applies.</item>
/// <item>Any other <see cref="PostgreSqlDistributedLockOptions.ConnectionString"/>: a provider-built
/// <see cref="NpgsqlDataSource"/> with the KeepAlive default applied, owned and disposed on container teardown.</item>
/// </list>
/// Nothing here is registered as the container's <see cref="NpgsqlDataSource"/>: that service belongs to the
/// application, and claiming it would hand the lock's pool to code that resolves it.
/// </remarks>
internal sealed class PostgresLockDataSource : IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly bool _owned;

    public PostgresLockDataSource(IOptions<PostgreSqlDistributedLockOptions> options)
    {
        var value = options.Value;

        if (value.DataSource is null && value.UseSharedConnectionPool)
        {
            ConnectionString = value.ConnectionString!;

            return;
        }

        _owned = value.DataSource is null;
        _dataSource = PostgresDataSourceFactory.CreateDataSource(value);
        ConnectionString = _dataSource.ConnectionString;
    }

    /// <summary>Gets the connection string of the pool the lock connections come from; the engine's pool key.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates an unopened connection the caller owns.</summary>
    public NpgsqlConnection CreateConnection()
    {
        return _dataSource?.CreateConnection() ?? new NpgsqlConnection(ConnectionString);
    }

    /// <summary>Creates and opens a connection the caller owns.</summary>
    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is not null)
        {
            return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        var connection = new NpgsqlConnection(ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return connection;
    }

    /// <summary>Creates an internally-owned lock connection for the multiplexing engine.</summary>
    public PostgresDatabaseConnection CreateLockConnection(TimeProvider timeProvider, int commandTimeoutSeconds)
    {
        return _dataSource is not null
            ? new PostgresDatabaseConnection(_dataSource, timeProvider, commandTimeoutSeconds)
            : new PostgresDatabaseConnection(ConnectionString, timeProvider, commandTimeoutSeconds);
    }

    /// <summary>Disposes a provider-built data source; a no-op for an injected one or the shared pool.</summary>
    public ValueTask DisposeAsync()
    {
        return _owned ? _dataSource!.DisposeAsync() : default;
    }

    /// <summary>Disposes a provider-built data source; a no-op for an injected one or the shared pool.</summary>
    public void Dispose()
    {
        if (_owned)
        {
            _dataSource!.Dispose();
        }
    }
}
