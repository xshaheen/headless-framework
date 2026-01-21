// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public class ConsumerServiceSelectorTests
{
    [Fact]
    public void should_select_candidates_from_registry()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("test.topic").Group("test-group").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().NotBeEmpty();
        candidates.Should().HaveCount(1);

        var descriptor = candidates[0];
        descriptor.ServiceTypeInfo.Should().Be(typeof(SelectorTestConsumer).GetTypeInfo());
        descriptor.ImplTypeInfo.Should().Be(typeof(SelectorTestConsumer).GetTypeInfo());
        descriptor.MethodInfo.Name.Should().Be(nameof(IConsume<object>.Consume));
        descriptor.TopicName.Should().Be("test.topic");
    }

    [Fact]
    public void should_build_group_name_with_prefix_and_version()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.GroupNamePrefix = "my-app";
            messaging.DefaultGroupName = "default";
            messaging.Version = "v2";
            messaging.Consumer<SelectorTestConsumer>().Topic("test.topic").Group("test-group").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        descriptor.GroupName.Should().Be("my-app.test-group.v2");
    }

    [Fact]
    public void should_use_default_group_when_not_specified()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default-group";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        descriptor.GroupName.Should().Be("default-group.v1");
    }

    [Fact]
    public void should_add_topic_name_prefix_when_configured()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.TopicNamePrefix = "my-app";
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var descriptor = candidates[0];
        descriptor.TopicNamePrefix.Should().Be("my-app");
    }

    [Fact]
    public void should_select_best_candidate_by_exact_match()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("orders.placed").Build();
            messaging.Consumer<AnotherSelectorConsumer>().Topic("orders.cancelled").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var best = selector.SelectBestCandidate("orders.placed", candidates);

        // then
        best.Should().NotBeNull();
        best.TopicName.Should().Be("orders.placed");
        best.ImplTypeInfo.Should().Be(typeof(SelectorTestConsumer).GetTypeInfo());
    }

    [Fact]
    public void should_return_null_when_no_candidate_matches()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("orders.placed").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var best = selector.SelectBestCandidate("non.existent.topic", candidates);

        // then
        best.Should().BeNull();
    }

    [Fact]
    public void should_match_wildcard_patterns()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("orders.*").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();
        var best = selector.SelectBestCandidate("orders.placed", candidates);

        // then
        best.Should().NotBeNull();
        best.TopicName.Should().Be("orders.*");
    }

    [Fact]
    public void should_handle_multiple_consumers_for_same_topic()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("orders.placed").Group("group1").Build();
            messaging.Consumer<AnotherSelectorConsumer>().Topic("orders.placed").Group("group2").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().HaveCount(2);
        var ordersCandidates = candidates.Where(c => c.TopicName == "orders.placed").ToList();
        ordersCandidates.Should().HaveCount(2);
    }

    [Fact]
    public void should_build_parameters_for_consume_method()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<SelectorTestConsumer>().Topic("test.topic").Build();
        });

        var provider = services.BuildServiceProvider();
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

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().BeEmpty();
    }
}

// Test message and consumer
public sealed record SelectorTestMessage(string Id);

public sealed class SelectorTestConsumer : IConsume<SelectorTestMessage>
{
    public ValueTask Consume(ConsumeContext<SelectorTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class AnotherSelectorConsumer : IConsume<SelectorTestMessage>
{
    public ValueTask Consume(ConsumeContext<SelectorTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
