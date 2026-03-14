using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RuntimeSubscriberTests : TestBase
{
    [Fact]
    public async Task should_resolve_default_runtime_subscription_metadata_for_named_handler()
    {
        await using var provider = _CreateProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var conventions = provider.GetRequiredService<IOptions<MessagingOptions>>().Value.Conventions;
        conventions.UseApplicationId("messaging-tests");
        conventions.UseVersion("v1");

        var handler = provider.GetRequiredService<NamedRuntimeHandler>();

        var handle = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            handler.HandleAsync,
            cancellationToken: AbortToken
        );

        handle.IsAttached.Should().BeTrue();
        handle.Topic.Should().Be(conventions.GetTopicName(typeof(RuntimeMessage)));
        handle
            .HandlerId.Should()
            .Be(
                MessagingConventions.GetDefaultRuntimeHandlerId(
                    typeof(NamedRuntimeHandler),
                    nameof(NamedRuntimeHandler.HandleAsync),
                    typeof(RuntimeMessage)
                )
            );
        handle.Group.Should().Be(conventions.GetGroupName(handle.HandlerId));
        handle.SubscriptionId.Should().NotBeNullOrEmpty();

        await handle.DisposeAsync();

        handle.IsAttached.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_fast_for_anonymous_runtime_handlers_without_explicit_handler_id()
    {
        await using var provider = _CreateProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();

        var act = () =>
            runtimeSubscriber
                .SubscribeAsync<RuntimeMessage>((_, _, _) => ValueTask.CompletedTask, cancellationToken: AbortToken)
                .AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Runtime subscriptions require a deterministic handler identity*");
    }

    [Fact]
    public async Task should_ignore_or_replace_duplicate_runtime_subscriptions_explicitly()
    {
        await using var provider = _CreateProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var cache = provider.GetRequiredService<MethodMatcherCache>();
        var firstHandler = provider.GetRequiredService<NamedRuntimeHandler>();
        var secondHandler = provider.GetRequiredService<AlternateNamedRuntimeHandler>();

        var first = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            firstHandler.HandleAsync,
            new RuntimeSubscriptionOptions { Topic = "runtime.duplicate", Group = "runtime.group" },
            AbortToken
        );

        var ignored = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            secondHandler.HandleAsync,
            new RuntimeSubscriptionOptions
            {
                Topic = "runtime.duplicate",
                Group = "runtime.group",
                DuplicateBehavior = RuntimeSubscriptionDuplicateBehavior.Ignore,
            },
            AbortToken
        );

        ignored.IsAttached.Should().BeFalse();

        var replaced = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            secondHandler.HandleAsync,
            new RuntimeSubscriptionOptions
            {
                Topic = "runtime.duplicate",
                Group = "runtime.group",
                DuplicateBehavior = RuntimeSubscriptionDuplicateBehavior.Replace,
            },
            AbortToken
        );

        cache.GetCandidatesMethodsOfGroupNameGrouped();
        cache.TryGetTopicExecutor("runtime.duplicate", "runtime.group", out var descriptor).Should().BeTrue();
        descriptor.Should().NotBeNull();
        descriptor!.HandlerId.Should().Be(replaced.HandlerId);

        await first.DisposeAsync();
        await replaced.DisposeAsync();
    }

    [Fact]
    public async Task should_keep_runtime_registry_consistent_under_concurrent_subscribe_and_unsubscribe()
    {
        await using var provider = _CreateProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var cache = provider.GetRequiredService<MethodMatcherCache>();
        var handlers = provider.GetServices<ConcurrentRuntimeHandler>().ToArray();

        var subscribeTasks = handlers.Select(
            (handler, index) =>
                runtimeSubscriber
                    .SubscribeAsync<RuntimeMessage>(
                        handler.HandleAsync,
                        new RuntimeSubscriptionOptions
                        {
                            Topic = $"runtime.concurrent.{index}",
                            Group = "runtime.concurrent",
                        },
                        AbortToken
                    )
                    .AsTask()
        );

        var handles = await Task.WhenAll(subscribeTasks);
        cache.GetAllTopics().Should().HaveCount(handlers.Length);

        await Task.WhenAll(handles.Select(handle => handle.DisposeAsync().AsTask()));

        cache.GetAllTopics().Should().BeEmpty();
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<NamedRuntimeHandler>();
        services.AddSingleton<AlternateNamedRuntimeHandler>();

        for (var i = 0; i < 10; i++)
        {
            services.AddSingleton<ConcurrentRuntimeHandler>();
        }

        services.AddMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
            options.UseConventions(c =>
            {
                c.UseApplicationId("messaging-tests");
                c.UseVersion("v1");
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed record RuntimeMessage(string Id);

    private sealed class NamedRuntimeHandler
    {
        public ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    private sealed class AlternateNamedRuntimeHandler
    {
        public ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    private sealed class ConcurrentRuntimeHandler
    {
        public ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }
}
