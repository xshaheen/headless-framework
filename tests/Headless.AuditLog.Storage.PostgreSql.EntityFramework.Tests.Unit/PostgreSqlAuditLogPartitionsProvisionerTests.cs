// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.AuditLog.PostgreSql;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PostgreSqlAuditLogPartitionsProvisionerTests : TestBase
{
    [Fact]
    public async Task should_report_the_public_kind_constant_for_a_partitioned_table()
    {
        // given - listing the provisioners opens no connection, so the database is never reached
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Partitioning = AuditLogPartitioning.Monthly);
            setup.UsePostgreSql("Host=localhost;Database=audit");
        });
        await using var provider = services.BuildServiceProvider();
        var source = provider
            .GetServices<IResourceProvisionerSource>()
            .OfType<PostgreSqlAuditLogPartitionsProvisionerSource>()
            .Single();

        // when
        var provisioners = await source.GetProvisionersAsync(AbortToken);

        // then
        // The value is what an operator passes to 'resources provision -k', so the constant must not change it.
        provisioners
            .Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SetupAuditLogPostgreSql.PartitionsResourceKind)
            .And.Be("AuditLogPartitions");
    }
}
