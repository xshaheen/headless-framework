// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class PostgreSqlStorageHealthCheckRegistrationTests : TestBase
{
    [Fact]
    public async Task should_contribute_a_storage_check_for_the_primary_postgresql_storage()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=app");
        });

        // then
        var registrations = await _StorageRegistrationsAsync(services);
        var registration = registrations.Should().ContainSingle().Subject;
        registration.Name.Should().Be("messaging-storage-postgresql-default");
        registration
            .Tags.Should()
            .Contain([
                HeadlessHealthCheckTags.Ready,
                HeadlessHealthCheckTags.Headless,
                HeadlessHealthCheckTags.Messaging,
                HeadlessHealthCheckTags.Database,
            ]);
    }

    [Fact]
    public async Task should_contribute_one_storage_check_per_postgresql_outbox()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=orders");
            setup.AddOutbox().UsePostgreSql("Host=localhost;Database=billing");
            setup.AddOutbox().UsePostgreSql(options => options.ConnectionString = "Host=localhost;Database=audit");
        });

        // then
        var registrations = await _StorageRegistrationsAsync(services);
        registrations
            .Select(registration => registration.Name)
            .Should()
            .BeEquivalentTo(
                "messaging-storage-postgresql-default",
                "messaging-storage-postgresql-outbox-1",
                "messaging-storage-postgresql-outbox-2"
            );
    }

    [Fact]
    public async Task should_fold_the_storage_check_into_the_sql_check_of_the_same_database()
    {
        // given the application's SQL connection and Messaging storage on one database, with different users
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=db1;Port=5432;Database=app;Username=app;Password=secret");

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UsePostgreSql("Host=db1;Port=5432;Database=app;Username=messaging;Password=other");
            setup.AddOutbox().UsePostgreSql("Host=db1;Port=5432;Database=billing");
        });

        // then - one probe for the shared database, which carries Messaging's tag; the other database keeps its own
        await using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations
            .Where(static x => x.Tags.Contains(HeadlessHealthCheckTags.Database))
            .Select(static x => x.Name)
            .Should()
            .BeEquivalentTo("sql-postgresql", "messaging-storage-postgresql-outbox-1");
        registrations
            .Single(static x => string.Equals(x.Name, "sql-postgresql", StringComparison.Ordinal))
            .Tags.Should()
            .Contain(HeadlessHealthCheckTags.Messaging);
    }

    private static async Task<List<HealthCheckRegistration>> _StorageRegistrationsAsync(ServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();

        return
        [
            .. provider
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations.Where(registration =>
                    registration.Name.StartsWith("messaging-storage-", StringComparison.Ordinal)
                ),
        ];
    }
}
