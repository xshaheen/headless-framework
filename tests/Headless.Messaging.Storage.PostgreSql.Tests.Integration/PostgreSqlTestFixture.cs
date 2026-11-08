// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;

namespace Tests;

/// <summary>
/// Collection fixture providing a PostgreSQL database of its own for integration tests.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlTestFixture : HeadlessPostgreSqlFixture, ICollectionFixture<PostgreSqlTestFixture>
{
    /// <summary>
    /// Gets a connection string whose <c>Host</c> lists the container twice, under a second loopback spelling when the
    /// host is a loopback address, so Npgsql treats it as a multi-host source and reports no data source until opened.
    /// </summary>
    public string MultiHostConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
            var host = builder.Host!;
            var second = host switch
            {
                "localhost" => "127.0.0.1",
                "127.0.0.1" => "localhost",
                _ => host,
            };
            builder.Host = $"{host},{second}";

            return builder.ConnectionString;
        }
    }
}
