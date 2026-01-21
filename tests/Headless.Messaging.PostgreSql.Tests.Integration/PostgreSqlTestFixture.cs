using Testcontainers.PostgreSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition]
public sealed class PostgreSqlTestFixture(IMessageSink messageSink)
    : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink),
        ICollectionFixture<PostgreSqlTestFixture>
{
    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("messages_test").WithUsername("postgres").WithPassword("postgres");
    }
}
