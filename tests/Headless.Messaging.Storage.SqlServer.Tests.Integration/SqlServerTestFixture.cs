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
/// history whose step checksum no longer matches after a step changed in place, or tables in an older shape.
/// </remarks>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerTestFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerTestFixture>,
        IAsyncLifetime
{
    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            TestMessagingSchema.DropSql(HeadlessStorageDefaults.Schema),
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
