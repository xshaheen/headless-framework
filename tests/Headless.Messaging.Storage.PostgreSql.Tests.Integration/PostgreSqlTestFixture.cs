// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// Collection fixture providing a PostgreSQL container for integration tests.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlTestFixture : HeadlessPostgreSqlFixture, ICollectionFixture<PostgreSqlTestFixture>
{
    /// <summary>Gets the PostgreSQL connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("messages_test").WithUsername("postgres").WithPassword("postgres");
    }
}
