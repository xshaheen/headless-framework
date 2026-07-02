// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// Builds the provider-owned <see cref="NpgsqlDataSource"/> from
/// <see cref="PostgresDistributedLockOptions.ConnectionString"/>, defaulting a TCP keepalive when the
/// connection string does not already specify one.
/// </summary>
/// <remarks>
/// Keepalive is complementary to the active connection monitor: TCP keepalive makes Npgsql's
/// <c>StateChange</c> event surface a silently-dropped idle holder faster, while the monitor's
/// bounded-timeout server-side probe is the authoritative active check. An injected
/// <see cref="PostgresDistributedLockOptions.DataSource"/> is the consumer's own object and is never
/// rebuilt here.
/// </remarks>
internal static class PostgresDataSourceFactory
{
    /// <summary>
    /// Returns the injected data source unchanged, or builds a new one from the connection string with a
    /// keepalive default applied when the string does not already set one.
    /// </summary>
    public static NpgsqlDataSource CreateDataSource(PostgresDistributedLockOptions options)
    {
        if (options.DataSource is not null)
        {
            return options.DataSource;
        }

        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);

        // KeepAlive of 0 is the Npgsql "disabled" sentinel, so a non-zero value means the connection
        // string already opted in and we must not clobber the consumer's chosen interval.
        if (builder.KeepAlive == 0 && options.KeepAlive > TimeSpan.Zero)
        {
            builder.KeepAlive = (int)options.KeepAlive.TotalSeconds;
        }

        return NpgsqlDataSource.Create(builder);
    }
}
