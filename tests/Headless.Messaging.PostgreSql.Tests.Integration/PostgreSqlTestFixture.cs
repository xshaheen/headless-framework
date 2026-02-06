// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

/// <summary>
/// Collection fixture providing a PostgreSQL container for integration tests.
/// Uses Testcontainers.PostgreSql for container lifecycle management.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlTestFixture(IMessageSink messageSink)
    : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink),
        ICollectionFixture<PostgreSqlTestFixture>
{
    /// <summary>Gets the PostgreSQL connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("messages_test").WithUsername("postgres").WithPassword("postgres");
    }
}
