// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Registration;

namespace Tests.Internal;

public sealed class ConsumerHostOwnershipTests : TestBase
{
    private const string _RuntimeIdentity = "orders.runtime-audit";

    [Fact]
    public void should_start_and_retry_every_consumer_on_an_unfiltered_host()
    {
        // given
        using var provider = _BuildHost(consumeOnly: null);
        var ownership = provider.GetRequiredService<IConsumerHostOwnership>();

        // when
        var started = provider.GetRequiredService<ConsumerRegistry>().GetAll().Where(ownership.Starts);

        // then
        started
            .Select(static consumer => consumer.ConsumerIdentity)
            .Should()
            .BeEquivalentTo([TestConsumers.InvoiceProjection, TestConsumers.Shipment, TestConsumers.PriceCache]);
        ownership.GetRetriedIdentities().Should().BeNull();
    }

    [Fact]
    public void should_start_the_selected_and_every_instance_consumers_on_a_consume_only_host()
    {
        // given
        using var provider = _BuildHost(consumeOnly: "orders.*");
        var ownership = provider.GetRequiredService<IConsumerHostOwnership>();

        // when
        var started = provider.GetRequiredService<ConsumerRegistry>().GetAll().Where(ownership.Starts);

        // then - the every-instance cache keeps per-process state, so ConsumeOnly never filters it
        started
            .Select(static consumer => consumer.ConsumerIdentity)
            .Should()
            .BeEquivalentTo([TestConsumers.Shipment, TestConsumers.PriceCache]);
    }

    [Fact]
    public void should_retry_only_the_selected_competing_identities_on_a_consume_only_host()
    {
        // given
        using var provider = _BuildHost(consumeOnly: "orders.*");

        // when
        var retried = provider.GetRequiredService<IConsumerHostOwnership>().GetRetriedIdentities();

        // then - every-instance consumers store no rows, so they add no identity
        retried.Should().Equal(TestConsumers.Shipment);
    }

    [Fact]
    public void should_retry_an_attached_competing_runtime_subscription_on_a_consume_only_host()
    {
        // given
        using var provider = _BuildHost(consumeOnly: "orders.*");
        var ownership = provider.GetRequiredService<IConsumerHostOwnership>();

        // when - ConsumeOnly never filters a runtime subscription, and only this host holds its delegate
        provider
            .GetRequiredService<IRuntimeConsumerRegistry>()
            .Register<InvoiceIssued>(
                static (_, _, _) => ValueTask.CompletedTask,
                new RuntimeSubscriptionOptions { Identity = _RuntimeIdentity, HandlerId = _RuntimeIdentity }
            );

        // then
        ownership.GetRetriedIdentities().Should().Equal(_RuntimeIdentity, TestConsumers.Shipment);
    }

    [Fact]
    public void should_not_retry_an_attached_every_instance_runtime_subscription()
    {
        // given
        using var provider = _BuildHost(consumeOnly: "orders.*");
        var ownership = provider.GetRequiredService<IConsumerHostOwnership>();

        // when
        provider
            .GetRequiredService<IRuntimeConsumerRegistry>()
            .Register<InvoiceIssued>(
                static (_, _, _) => ValueTask.CompletedTask,
                new RuntimeSubscriptionOptions
                {
                    Identity = _RuntimeIdentity,
                    HandlerId = _RuntimeIdentity,
                    EveryInstance = true,
                }
            );

        // then
        ownership.GetRetriedIdentities().Should().Equal(TestConsumers.Shipment);
    }

    private static ServiceProvider _BuildHost(string? consumeOnly)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostControlProbe>();
        services.ConfigureMessaging(messaging =>
        {
            messaging.AddModule<BillingModule>().AddModule<OrdersModule>().AddModule<BillingPriceCacheModule>();
            messaging.Message<InvoiceIssued>("billing.invoice-issued");
            messaging.Message<OrderShipped>("orders.order-shipped");
            messaging.Message<PriceChanged>("billing.price-changed");
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            if (consumeOnly is not null)
            {
                setup.ConsumeOnly(consumeOnly);
            }
        });

        return services.BuildServiceProvider();
    }
}
