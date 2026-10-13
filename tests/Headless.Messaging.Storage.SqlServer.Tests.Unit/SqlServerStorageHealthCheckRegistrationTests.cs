// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SqlServerStorageHealthCheckRegistrationTests : TestBase
{
    [Fact]
    public async Task should_contribute_a_storage_check_for_the_primary_sqlserver_storage()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServer("Server=localhost;Database=app");
        });

        // then
        var registrations = await _StorageRegistrationsAsync(services);
        var registration = registrations.Should().ContainSingle().Subject;
        registration.Name.Should().Be("messaging-storage-sqlserver-default");
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
    public async Task should_contribute_one_storage_check_per_sqlserver_outbox()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServer("Server=localhost;Database=orders");
            setup.AddOutbox().UseSqlServer("Server=localhost;Database=billing");
            setup.AddOutbox().UseSqlServer(options => options.ConnectionString = "Server=localhost;Database=audit");
        });

        // then
        var registrations = await _StorageRegistrationsAsync(services);
        registrations
            .Select(registration => registration.Name)
            .Should()
            .BeEquivalentTo(
                "messaging-storage-sqlserver-default",
                "messaging-storage-sqlserver-outbox-1",
                "messaging-storage-sqlserver-outbox-2"
            );
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
