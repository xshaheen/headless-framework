// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class PostgreSqlOutboxRegistrationTests : TestBase
{
    private const string _Billing = "Host=localhost;Database=billing_db";
    private const string _Shipping = "Host=localhost;Database=shipping_db";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_keep_each_outbox_name_bound_to_its_own_database_whatever_the_registration_order(
        bool shippingFirst
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=orders_db");

            if (shippingFirst)
            {
                setup.AddOutbox("shipping").UsePostgreSql(_Shipping);
                setup.AddOutbox("billing").UsePostgreSql(options => options.ConnectionString = _Billing);
            }
            else
            {
                setup.AddOutbox("billing").UsePostgreSql(options => options.ConnectionString = _Billing);
                setup.AddOutbox("shipping").UsePostgreSql(_Shipping);
            }
        });

        // then — the store, its health check, and its database travel together under the chosen name
        await using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<MessagingOutboxes>()
            .Secondaries.Select(outbox =>
            {
                using var connection = outbox.Relational.CreateIdentityConnection();

                return (outbox.Name, connection.Database);
            })
            .Should()
            .BeEquivalentTo([("billing", "billing_db"), ("shipping", "shipping_db")]);
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name)
            .Where(name => name.StartsWith("messaging-storage-postgresql-outbox-", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo(
                "messaging-storage-postgresql-outbox-billing",
                "messaging-storage-postgresql-outbox-shipping"
            );
    }
}
