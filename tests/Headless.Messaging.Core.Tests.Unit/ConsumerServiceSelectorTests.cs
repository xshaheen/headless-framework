// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
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
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message
                    .MessageName("test.messageName")
                    .Consumer<SelectorTestConsumer>(consumer => consumer.Group("test-group"))
            );
            messaging.Options.DefaultGroupName = "default";
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
        descriptor.ServiceTypeInfo.Should().Be(typeof(SelectorTestConsumer).GetTypeInfo());
        descriptor.ImplTypeInfo.Should().Be(typeof(SelectorTestConsumer).GetTypeInfo());
        descriptor.MethodInfo.Name.Should().Be(nameof(IConsume<>.ConsumeAsync));
        descriptor.MessageName.Should().Be("test.messageName");
    }

    [Fact]
    public void should_use_explicit_group_without_appending_convention_suffixes()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message
                    .MessageName("test.messageName")
                    .Consumer<SelectorTestConsumer>(consumer => consumer.Group("test-group"))
            );
            messaging.UseConventions(conventions =>
            {
                conventions.UseApplicationId("my-app");
                conventions.UseVersion("v2");
            });
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        descriptor.GroupName.Should().Be("test-group");
    }

    [Fact]
    public void should_use_default_group_when_not_specified()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message.MessageName("test.messageName").Consumer<SelectorTestConsumer>()
            );
            messaging.UseConventions(conventions =>
            {
                conventions.UseApplicationId("default-app");
                conventions.UseVersion("v1");
            });
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        var conventions = new MessagingConventions().UseApplicationId("default-app").UseVersion("v1");
        var handlerId = MessagingConventions.GetDefaultHandlerId(
            typeof(SelectorTestConsumer),
            typeof(SelectorTestMessage)
        );
        descriptor.GroupName.Should().Be(conventions.GetGroupName(handlerId));
    }

    [Fact]
    public void should_add_topic_name_prefix_when_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message.MessageName("test.messageName").Consumer<SelectorTestConsumer>()
            );
            messaging.Options.MessageNamePrefix = "my-app";
            messaging.Options.DefaultGroupName = "default";
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        descriptor.MessageNamePrefix.Should().Be("my-app");
    }

    [Fact]
    public void should_select_best_candidate_by_exact_match()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message.MessageName("orders.placed").Consumer<SelectorTestConsumer>()
            );
            messaging.Bus.ForMessage<AnotherSelectorTestMessage>(message =>
                message.MessageName("orders.cancelled").Consumer<AnotherSelectorConsumer>()
            );
            messaging.Options.DefaultGroupName = "default";
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
        best.ImplTypeInfo.Should().Be(typeof(SelectorTestConsumer).GetTypeInfo());
    }

    [Fact]
    public void should_return_null_when_no_candidate_matches()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message.MessageName("orders.placed").Consumer<SelectorTestConsumer>()
            );
            messaging.Options.DefaultGroupName = "default";
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
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Options.DefaultGroupName = "default";
            messaging.Options.Version = "v1";
            messaging.RegisterConsumer(
                typeof(SelectorTestConsumer),
                typeof(SelectorTestMessage),
                "orders.*",
                group: null,
                concurrency: 1,
                lane: MessageLane.Bus
            );
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
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
            {
                message.MessageName("orders.placed");
                message.Consumer<SelectorTestConsumer>(consumer => consumer.Group("group1"));
                message.Consumer<SecondSelectorConsumer>(consumer => consumer.Group("group2"));
            });
            messaging.Options.DefaultGroupName = "default";
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
    public void should_build_parameters_for_consume_method()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message.MessageName("test.messageName").Consumer<SelectorTestConsumer>()
            );
            messaging.Options.DefaultGroupName = "default";
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var descriptor = candidates[0];

        // then
        descriptor.Parameters.Should().HaveCount(2);
        descriptor
            .Parameters.Should()
            .Contain(p => p.ParameterType == typeof(ConsumeContext<SelectorTestMessage>) && !p.IsFromMessaging);
        descriptor.Parameters.Should().Contain(p => p.ParameterType == typeof(CancellationToken) && p.IsFromMessaging);
    }

    [Fact]
    public void should_return_empty_when_no_registry()
    {
        // given - no registry registered
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MessagingOptions>(opt =>
        {
            opt.DefaultGroupName = "default";
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
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message
                    .MessageName("test.messageName")
                    .Consumer<SelectorTestConsumer>(consumer => consumer.Concurrency(5))
            );
            messaging.Options.DefaultGroupName = "default";
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
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<SelectorTestMessage>(message =>
                message.MessageName("test.messageName").Consumer<SelectorTestConsumer>()
            );
            messaging.Options.DefaultGroupName = "default";
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

public sealed class SelectorTestConsumer : IConsume<SelectorTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<SelectorTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class SecondSelectorConsumer : IConsume<SelectorTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<SelectorTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

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
