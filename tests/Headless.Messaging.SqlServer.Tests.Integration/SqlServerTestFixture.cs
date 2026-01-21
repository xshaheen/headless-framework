using Testcontainers.MsSql;
using Testcontainers.Xunit;
using Xunit.Sdk;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition]
public sealed class SqlServerTestFixture(IMessageSink messageSink)
    : ContainerFixture<MsSqlBuilder, MsSqlContainer>(messageSink),
        ICollectionFixture<SqlServerTestFixture>
{
    protected override MsSqlBuilder Configure()
    {
        return base.Configure().WithPassword("YourStrong@Passw0rd");
    }
}
