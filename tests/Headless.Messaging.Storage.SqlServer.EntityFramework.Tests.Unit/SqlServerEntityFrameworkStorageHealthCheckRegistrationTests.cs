// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SqlServerEntityFrameworkStorageHealthCheckRegistrationTests : TestBase
{
    [Fact]
    public async Task should_contribute_storage_checks_for_entity_framework_primary_and_outbox()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OrdersDbContext>(builder => builder.UseSqlServer("Server=localhost;Database=orders"));
        services.AddDbContext<BillingDbContext>(builder => builder.UseSqlServer("Server=localhost;Database=billing"));

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServerEntityFramework<OrdersDbContext>();
            setup.AddOutbox().UseSqlServerEntityFramework<BillingDbContext>();
        });

        // then
        await using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name)
            .Where(name => name.StartsWith("messaging-storage-", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo("messaging-storage-sqlserver-default", "messaging-storage-sqlserver-outbox-1");
    }

    private sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options);

    private sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options);
}
