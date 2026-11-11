// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Headless.Fencing.Benchmarks;

public enum FencingProvider
{
    PostgreSql,
    SqlServer,
}

/// <summary>
/// A throwaway database for one benchmark process. It lives on the server the provider's environment variable names
/// (<c>HEADLESS_BENCH_POSTGRES</c> or <c>HEADLESS_BENCH_SQLSERVER</c>, an admin connection string) or, when that is
/// unset, on a container started for the run. Either way the database is created here and dropped on dispose, so a
/// run never touches existing data.
/// </summary>
internal sealed class BenchmarkDatabase : IAsyncDisposable
{
    // The images the integration fixtures pin (Headless.Testing.Testcontainers TestImages); the digests keep a fresh
    // pull from changing server behavior between runs. SQL Server runs under x86_64 emulation on an ARM64 host, so
    // only its relative figures are meaningful there.
    private const string _PostgreSqlImage =
        "postgres:18.1-alpine3.23@sha256:aa6eb304ddb6dd26df23d05db4e5cb05af8951cda3e0dc57731b771e0ef4ab29";

    private const string _SqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU19-ubuntu-22.04@sha256:147ee765ff1db3b86ce6ec05908e51fd0dab2feda5dd85b2721f28c77ca305eb";

    private readonly IAsyncDisposable? _container;
    private readonly string _serverConnectionString;
    private readonly string _name;

    private BenchmarkDatabase(
        FencingProvider provider,
        IAsyncDisposable? container,
        string serverConnectionString,
        string name,
        string connectionString
    )
    {
        Provider = provider;
        _container = container;
        _serverConnectionString = serverConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    public FencingProvider Provider { get; }

    public string ConnectionString { get; }

    public static async Task<BenchmarkDatabase> CreateAsync(FencingProvider provider)
    {
        var name = $"fencing_bench_{Guid.NewGuid():N}";
        IAsyncDisposable? container = null;

        try
        {
            string server;

            if (provider == FencingProvider.PostgreSql)
            {
                server = Environment.GetEnvironmentVariable("HEADLESS_BENCH_POSTGRES") ?? "";

                if (server.Length == 0)
                {
                    var postgres = new PostgreSqlBuilder(_PostgreSqlImage).Build();
                    container = postgres;
                    await postgres.StartAsync();
                    server = postgres.GetConnectionString();
                }

                await _ExecuteAsync(provider, server, $"CREATE DATABASE \"{name}\"");

                // Pooling stays on, as in production: the own-connection read checks a pooled connection out per call.
                var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;

                return new BenchmarkDatabase(provider, container, server, name, connectionString);
            }

            server = Environment.GetEnvironmentVariable("HEADLESS_BENCH_SQLSERVER") ?? "";

            if (server.Length == 0)
            {
                var sqlServer = new MsSqlBuilder(_SqlServerImage).Build();
                container = sqlServer;
                await sqlServer.StartAsync();
                server = sqlServer.GetConnectionString();
            }

            await _ExecuteAsync(provider, server, $"CREATE DATABASE [{name}]");
            var sqlConnectionString = new SqlConnectionStringBuilder(server)
            {
                InitialCatalog = name,
                TrustServerCertificate = true,
            }.ConnectionString;

            return new BenchmarkDatabase(provider, container, server, name, sqlConnectionString);
        }
        catch
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            throw;
        }
    }

    public DbConnection CreateConnection()
    {
        return Provider == FencingProvider.PostgreSql
            ? new NpgsqlConnection(ConnectionString)
            : new SqlConnection(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Provider == FencingProvider.PostgreSql)
            {
                NpgsqlConnection.ClearAllPools();
                await _ExecuteAsync(
                    Provider,
                    _serverConnectionString,
                    $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)"
                );
            }
            else
            {
                SqlConnection.ClearAllPools();
                await _ExecuteAsync(
                    Provider,
                    _serverConnectionString,
                    $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]"
                );
            }
        }
        finally
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    private static async Task _ExecuteAsync(FencingProvider provider, string connectionString, string sql)
    {
        await using DbConnection connection =
            provider == FencingProvider.PostgreSql
                ? new NpgsqlConnection(connectionString)
                : new SqlConnection(
                    new SqlConnectionStringBuilder(connectionString) { TrustServerCertificate = true }.ConnectionString
                );
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The only interpolated value is the database name this class generated from a Guid.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }
}
