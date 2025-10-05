// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class NpgsqlTestFixture(IMessageSink messageSink)
    : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink),
        ICollectionFixture<NpgsqlTestFixture>
{
    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
    {
        return builder
            .WithDatabase("framework_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithPortBinding(5432);
    }
}
