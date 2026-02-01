// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.MsSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

/// <summary>
/// Collection fixture providing a SQL Server container for integration tests.
/// Uses Testcontainers.MsSql for container lifecycle management.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerTestFixture(IMessageSink messageSink)
    : ContainerFixture<MsSqlBuilder, MsSqlContainer>(messageSink),
        ICollectionFixture<SqlServerTestFixture>
{
    /// <summary>Gets the SQL Server connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    protected override MsSqlBuilder Configure()
    {
        return base.Configure().WithPassword("YourStrong@Passw0rd");
    }
}
