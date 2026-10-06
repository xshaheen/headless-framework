// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SqlHealthCheckRegistrationTests
{
    [Fact]
    public void should_contribute_sql_postgresql_check_when_add_postgresql_sql()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddPostgreSqlSql("Host=localhost;Database=app");

        // then
        _Registration(services).Name.Should().Be("sql-postgresql");
    }

    [Fact]
    public void should_contribute_sql_sqlserver_check_when_add_sqlserver_sql()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddSqlServerSql(_ => "Server=localhost;Database=app");

        // then
        _Registration(services).Name.Should().Be("sql-sqlserver");
    }

    private static HealthCheckRegistration _Registration(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .ContainSingle()
            .Subject;

        registration.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Database]);

        return registration;
    }
}
