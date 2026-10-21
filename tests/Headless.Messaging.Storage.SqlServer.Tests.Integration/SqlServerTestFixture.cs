// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// Collection fixture providing a SQL Server container for integration tests.
/// Uses the shared <see cref="HeadlessSqlServerFixture"/> for ARM64/x64 compatibility.
/// </summary>
/// <remarks>
/// The container is reused across runs of this checkout, so it starts by dropping the default messaging schema and its
/// history. Tests that apply the schema without dropping it first would otherwise meet whatever an older run left: a
/// history whose step checksum no longer matches after a step changed in place, or tables in an older shape. The
/// real-membership dead-owner test also creates the coordination tables in that schema, so they go first, or the
/// schema cannot be dropped.
/// </remarks>
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

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"""
            DROP TABLE IF EXISTS [{HeadlessStorageDefaults.Schema}].CoordinationLiveness;
            DROP TABLE IF EXISTS [{HeadlessStorageDefaults.Schema}].CoordinationDescriptor;
            DROP TABLE IF EXISTS [{HeadlessStorageDefaults.Schema}].CoordinationNodeGeneration;
            {TestMessagingSchema.DropSql(HeadlessStorageDefaults.Schema)}
            """,
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
