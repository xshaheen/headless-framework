// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// Collection fixture providing a SQL Server container for integration tests.
/// Uses the shared <see cref="HeadlessSqlServerFixture"/> for ARM64/x64 compatibility.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerTestFixture : HeadlessSqlServerFixture, ICollectionFixture<SqlServerTestFixture>
{
    /// <summary>
    /// Gets the host port the container publishes, read from the connection string's <c>host,port</c> data source, so
    /// a test can put a proxy in front of the database.
    /// </summary>
    public int HostPort
    {
        get
        {
            var dataSource = new SqlConnectionStringBuilder(ConnectionString).DataSource;
            return int.Parse(dataSource[(dataSource.LastIndexOf(',') + 1)..], CultureInfo.InvariantCulture);
        }
    }
}
