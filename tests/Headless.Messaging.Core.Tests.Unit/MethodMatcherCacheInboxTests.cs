// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MethodMatcherCacheInboxTests : TestBase
{
    [Theory]
    [InlineData("orders.created", "consumer", "1", MessageLane.Bus)]
    [InlineData("preorders", "consumer", "1", MessageLane.Bus)]
    [InlineData("Orders", "consumer", "1", MessageLane.Bus)]
    [InlineData("orders", "other-consumer", "1", MessageLane.Bus)]
    [InlineData("orders", "Consumer", "1", MessageLane.Bus)]
    [InlineData("orders", "consumer", "2", MessageLane.Bus)]
    [InlineData("orders", "consumer", "1", MessageLane.Queue)]
    public void should_reject_inbox_when_any_persisted_identity_component_differs(
        string contract,
        string consumer,
        string version,
        MessageLane lane
    )
    {
        using var provider = _CreateProvider(MessageLane.Bus);
        var cache = provider.GetRequiredService<MethodMatcherCache>();

        cache.TryGetInboxExecutor(consumer, contract, version, lane, out var descriptor).Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public void should_resolve_exact_inbox_identity_to_current_group(MessageLane lane)
    {
        using var provider = _CreateProvider(lane);
        var cache = provider.GetRequiredService<MethodMatcherCache>();

        cache.TryGetInboxExecutor("consumer", "orders", "1", lane, out var descriptor).Should().BeTrue();
        descriptor.Should().NotBeNull();
        descriptor.GroupName.Should().Be("current-group");
        descriptor.Lane.Should().Be(lane);
    }

    [Fact]
    public void should_not_resolve_inbox_through_another_consumers_warmed_subscription_cache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.RegisterConsumer(
                typeof(AnotherSelectorConsumer),
                typeof(AnotherSelectorTestMessage),
                "orders.*",
                "current-group",
                1,
                MessageLane.Bus,
                "other-consumer",
                "1"
            );
            setup.Bus.ForMessage<SelectorTestMessage>(message =>
                message
                    .Contract("orders")
                    .Consumer<SelectorTestConsumer>(consumer =>
                        consumer.ConsumerIdentity("consumer").Group("current-group")
                    )
            );
        });
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<MethodMatcherCache>();

        cache
            .TryGetMessageNameExecutor("orders.created", "current-group", MessageLane.Bus, out var subscription)
            .Should()
            .BeTrue();
        subscription.Should().NotBeNull();
        subscription.ConsumerIdentity.Should().Be("other-consumer");

        cache
            .TryGetInboxExecutor("consumer", "orders.created", "1", MessageLane.Bus, out var descriptor)
            .Should()
            .BeFalse();
        descriptor.Should().BeNull();
        cache.TryGetInboxExecutor("consumer", "orders", "1", MessageLane.Bus, out var exact).Should().BeTrue();
        exact.Should().NotBeNull();
        exact.ConsumerIdentity.Should().Be("consumer");
    }

    [Theory]
    [InlineData(MessageLane.Bus, "orders.*")]
    [InlineData(MessageLane.Bus, "orders.#")]
    [InlineData(MessageLane.Queue, "orders.*")]
    [InlineData(MessageLane.Queue, "orders.#")]
    public void should_reject_wildcards_in_public_inbox_contract_registrations(MessageLane lane, string contract)
    {
        var act = () =>
        {
            using var provider = _CreateProvider(lane, contract);
            provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        };

        act.Should().Throw<ArgumentException>().WithMessage("*contains invalid character*");
    }

    private static ServiceProvider _CreateProvider(MessageLane lane, string contract = "orders")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            if (lane is MessageLane.Bus)
            {
                setup.Bus.ForMessage<SelectorTestMessage>(message =>
                    message
                        .Contract(contract)
                        .Consumer<SelectorTestConsumer>(consumer =>
                            consumer.ConsumerIdentity("consumer").Group("current-group")
                        )
                );
            }
            else
            {
                setup.Queue.ForMessage<SelectorTestMessage>(message =>
                    message
                        .Contract(contract)
                        .Consumer<SelectorTestConsumer>(consumer =>
                            consumer.ConsumerIdentity("consumer").Group("current-group")
                        )
                );
            }
        });
        return services.BuildServiceProvider();
    }
}
