// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition(nameof(NpgsqlTestFixture))]
public sealed class NpgsqlTestFixture(IMessageSink messageSink)
    : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink),
        ICollectionFixture<NpgsqlTestFixture>
{
    protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
    {
        return builder
            .WithDatabase("NpgsqlTest")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithPortBinding(5432);
    }
}
