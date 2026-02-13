// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class RuntimeMessageSubscriberTests : TestBase
{
    [Fact]
    public async Task should_subscribe_runtime_handler_and_invoke_with_scoped_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TestScopedDependency>();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var invoker = provider.GetRequiredService<ISubscribeInvoker>();

        RuntimeTestMessage? consumedMessage = null;
        Guid dependencyId = Guid.Empty;

        var subscription = await runtimeSubscriber.SubscribeAsync<RuntimeTestMessage>(
            (sp, context, ct) =>
            {
                consumedMessage = context.Message;
                dependencyId = sp.GetRequiredService<TestScopedDependency>().Id;
                return ValueTask.CompletedTask;
            },
            topic: "runtime.topic",
            group: "runtime.group",
            handlerId: "runtime-handler",
            cancellationToken: AbortToken
        );

        var descriptor = selector
            .SelectCandidates()
            .Single(x => x.RuntimeSubscriptionKey == subscription.Key && x.RuntimeHandler is not null);

        var mediumMessage = _CreateMediumMessage(
            new RuntimeTestMessage("abc"),
            subscription.Key.Topic,
            subscription.Key.Group,
            messageId: Guid.NewGuid().ToString("N")
        );

        // when
        await invoker.InvokeAsync(new ConsumerContext(descriptor, mediumMessage), AbortToken);

        // then
        consumedMessage.Should().NotBeNull();
        consumedMessage!.Value.Should().Be("abc");
        dependencyId.Should().NotBe(Guid.Empty);

        await provider.GetRequiredService<IDispatcher>().DisposeAsync();
    }

    [Fact]
    public async Task should_unsubscribe_runtime_handler()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        var subscription = await runtimeSubscriber.SubscribeAsync<RuntimeTestMessage>(
            (_, _, _) => ValueTask.CompletedTask,
            topic: "runtime.topic",
            group: "runtime.group",
            handlerId: "runtime-handler",
            cancellationToken: AbortToken
        );

        // when
        var removed = await runtimeSubscriber.UnsubscribeAsync(subscription.Key, AbortToken);

        // then
        removed.Should().BeTrue();
        runtimeSubscriber.ListSubscriptions().Should().NotContain(subscription.Key);
        selector.SelectCandidates().Should().NotContain(x => x.RuntimeSubscriptionKey == subscription.Key);
    }

    [Fact]
    public async Task should_unsubscribe_runtime_handler_when_subscription_is_disposed()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        var subscription = await runtimeSubscriber.SubscribeAsync<RuntimeTestMessage>(
            (_, _, _) => ValueTask.CompletedTask,
            topic: "runtime.topic",
            group: "runtime.group",
            handlerId: "runtime-handler",
            cancellationToken: AbortToken
        );

        // when
        await subscription.DisposeAsync();

        // then
        runtimeSubscriber.ListSubscriptions().Should().NotContain(subscription.Key);
        selector.SelectCandidates().Should().NotContain(x => x.RuntimeSubscriptionKey == subscription.Key);
    }

    [Fact]
    public async Task should_reject_duplicate_runtime_route()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();

        await runtimeSubscriber.SubscribeAsync<RuntimeTestMessage>(
            (_, _, _) => ValueTask.CompletedTask,
            topic: "runtime.topic",
            group: "runtime.group",
            handlerId: "handler-1",
            cancellationToken: AbortToken
        );

        // when
        var act = () =>
            runtimeSubscriber
                .SubscribeAsync<RuntimeTestMessage>(
                    (_, _, _) => ValueTask.CompletedTask,
                    topic: "runtime.topic",
                    group: "runtime.group",
                    handlerId: "handler-2",
                    cancellationToken: AbortToken
                )
                .AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*runtime routes must be unique*");
    }

    [Fact]
    public async Task should_reject_duplicate_runtime_route_when_only_casing_differs()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();

        await runtimeSubscriber.SubscribeAsync<RuntimeTestMessage>(
            (_, _, _) => ValueTask.CompletedTask,
            topic: "runtime.topic",
            group: "runtime.group",
            handlerId: "handler-1",
            cancellationToken: AbortToken
        );

        // when
        var act = () =>
            runtimeSubscriber
                .SubscribeAsync<RuntimeTestMessage>(
                    (_, _, _) => ValueTask.CompletedTask,
                    topic: "Runtime.Topic",
                    group: "Runtime.Group",
                    handlerId: "handler-2",
                    cancellationToken: AbortToken
                )
                .AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*runtime routes must be unique*");
    }

    [Fact]
    public async Task should_reject_runtime_route_when_static_consumer_exists()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.Consumer<StaticRuntimeConflictConsumer>().Topic("runtime.topic").Group("runtime.group").Build();
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();

        // when
        var act = () =>
            runtimeSubscriber
                .SubscribeAsync<RuntimeTestMessage>(
                    (_, _, _) => ValueTask.CompletedTask,
                    topic: "runtime.topic",
                    group: "runtime.group",
                    handlerId: "runtime-handler",
                    cancellationToken: AbortToken
                )
                .AsTask();

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*route is already used by a class consumer*");
    }

    [Fact]
    public async Task should_apply_convention_defaults_when_topic_and_group_are_omitted()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(options =>
        {
            options.DefaultGroupName = "default";
            options.Version = "v1";
            options.ConfigureConventions(conventions =>
            {
                conventions.UseKebabCaseTopics();
                conventions.WithDefaultGroup("convention-group");
            });
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeMessageSubscriber>();

        // when
        var subscription = await runtimeSubscriber.SubscribeAsync<ConventionRuntimeMessage>(
            (_, _, _) => ValueTask.CompletedTask,
            cancellationToken: AbortToken
        );

        // then
        subscription.Key.Topic.Should().Be("convention-runtime-message");
        subscription.Key.Group.Should().Be("convention-group.v1");
    }

    private static MediumMessage _CreateMediumMessage<T>(
        T message,
        string topic,
        string group,
        string messageId
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = messageId,
            [Headers.MessageName] = topic,
            [Headers.Group] = group,
        };

        var json = JsonSerializer.Serialize(message);

        return new MediumMessage
        {
            DbId = "runtime-test",
            Origin = new Message(headers, json),
            Content = json,
            Added = DateTime.UtcNow,
        };
    }

    private sealed class TestScopedDependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed record RuntimeTestMessage(string Value);

    private sealed record ConventionRuntimeMessage(string Value);

    private sealed class StaticRuntimeConflictConsumer : IConsume<RuntimeTestMessage>
    {
        public ValueTask Consume(ConsumeContext<RuntimeTestMessage> context, CancellationToken cancellationToken)
        {
            _ = context;
            return ValueTask.CompletedTask;
        }
    }
}
