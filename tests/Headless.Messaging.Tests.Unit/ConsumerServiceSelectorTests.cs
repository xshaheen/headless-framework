// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public sealed class ConsumerServiceSelectorTests
{
    [Fact]
    public void should_select_candidates_from_registry()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("test.messageName"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().NotBeEmpty();
        candidates.Should().ContainSingle();

        var descriptor = candidates[0];
        descriptor.ConsumerType.Should().Be<SelectorTestConsumer>();
        descriptor.MethodName.Should().Be(nameof(IConsume<>.ConsumeAsync));
        descriptor.MessageName.Should().Be("test.messageName");
    }

    [Fact]
    public void should_add_topic_name_prefix_when_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("test.messageName"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.Options.MessageNamePrefix = "my-app";
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        descriptor.MessageName.Should().Be("my-app.test.messageName");
    }

    [Fact]
    public void should_select_best_candidate_by_exact_match()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("orders.placed"));
        services.ConfigureMessaging(messaging => messaging.Message<AnotherSelectorTestMessage>("orders.cancelled"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.AddConsumer<AnotherSelectorConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var best = selector.SelectBestCandidate("orders.placed", candidates);

        // then
        best.Should().NotBeNull();
        best.MessageName.Should().Be("orders.placed");
        best.ConsumerType.Should().Be<SelectorTestConsumer>();
    }

    [Fact]
    public void should_return_null_when_no_candidate_matches()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("orders.placed"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var best = selector.SelectBestCandidate("non.existent.messageName", candidates);

        // then
        best.Should().BeNull();
    }

    [Fact]
    public void should_match_wildcard_patterns()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging => messaging.Options.Version = "v1");

        // A wildcard subscription has no message contract, so it is seeded into the registry before the host's
        // registrations fold into it.
        services.AddSingleton(sp =>
        {
            var registry = new ConsumerRegistry();
            registry.Register(
                new ConsumerMetadata(
                    typeof(SelectorTestMessage),
                    typeof(SelectorTestConsumer),
                    "orders.*",
                    1,
                    MessageLane.Bus,
                    "tests.selector-wildcard",
                    "v1"
                )
            );
            return SetupMessaging.BuildConsumerRegistry(sp, registry);
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var best = selector.SelectBestCandidate("orders.placed", candidates);

        // then
        best.Should().NotBeNull();
        best.MessageName.Should().Be("orders.*");
    }

    [Fact]
    public void should_handle_multiple_consumers_for_same_topic()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("orders.placed"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.AddConsumer<SecondSelectorConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().HaveCount(2);
        var ordersCandidates = candidates
            .Where(c => string.Equals(c.MessageName, "orders.placed", StringComparison.Ordinal))
            .ToList();
        ordersCandidates.Should().HaveCount(2);
    }

    [Fact]
    public void should_describe_the_consumed_message_type()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("test.messageName"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var descriptor = candidates[0];

        // then
        descriptor.MessageType.Should().Be<SelectorTestMessage>();
    }

    [Fact]
    public void should_return_empty_when_no_registry()
    {
        // given - no registry registered
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MessagingOptions>(opt =>
        {
            opt.Version = "v1";
        });
        services.TryAddSingleton<IConsumerServiceSelector, ConsumerServiceSelector>();

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().BeEmpty();
    }

    [Fact]
    public void should_propagate_concurrency_setting()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("test.messageName"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.Tune("tests.selector.primary", consumer => consumer.Concurrency(5));
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().ContainSingle();
        candidates[0].Concurrency.Should().Be(5);
    }

    [Fact]
    public void should_default_concurrency_to_one()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>("test.messageName"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<SelectorTestConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().ContainSingle();
        candidates[0].Concurrency.Should().Be(1);
    }
}

// Test message and consumer
public sealed record SelectorTestMessage(string Id);

public sealed record AnotherSelectorTestMessage(string Id);

[BusConsumer("tests.selector.primary")]
public sealed class SelectorTestConsumer : IConsume<SelectorTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<SelectorTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

[BusConsumer("tests.selector.secondary")]
public sealed class SecondSelectorConsumer : IConsume<SelectorTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<SelectorTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

[BusConsumer("tests.selector.another")]
public sealed class AnotherSelectorConsumer : IConsume<AnotherSelectorTestMessage>
{
    public ValueTask ConsumeAsync(
        ConsumeContext<AnotherSelectorTestMessage> context,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.CompletedTask;
    }
}
