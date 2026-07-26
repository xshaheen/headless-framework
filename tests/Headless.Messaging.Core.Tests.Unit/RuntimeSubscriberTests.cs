using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0060 // Remove unused parameter
namespace Tests;

public sealed class RuntimeSubscriberTests : TestBase
{
    [Fact]
    public void runtime_registry_uses_the_bus_lane_for_names_descriptors_and_invoker_identity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForMessage<RuntimeMessage>(message => message.MessageName("runtime.bus"));
            setup.Queue.ForMessage<RuntimeMessage>(message => message.MessageName("runtime.queue"));
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IRuntimeConsumerRegistry>();
        var handler = new NamedRuntimeHandler();

        var result = registry.Register<RuntimeMessage>(handler.HandleAsync);

        result.Lane.Should().Be(MessageLane.Bus);
        result.MessageName.Should().Be("runtime.bus");
        registry.GetDescriptors().Should().ContainSingle().Which.Lane.Should().Be(MessageLane.Bus);
        registry
            .TryGetInvoker(result.MessageName, result.Group, result.HandlerId, MessageLane.Bus, out _)
            .Should()
            .BeTrue();
        registry
            .TryGetInvoker(result.MessageName, result.Group, result.HandlerId, MessageLane.Queue, out _)
            .Should()
            .BeFalse();
    }

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
        handle.MessageName.Should().Be(conventions.GetMessageName(typeof(RuntimeMessage)));
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
            new RuntimeSubscriptionOptions { MessageName = "runtime.duplicate", Group = "runtime.group" },
            AbortToken
        );

        var ignored = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            secondHandler.HandleAsync,
            new RuntimeSubscriptionOptions
            {
                MessageName = "runtime.duplicate",
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
                MessageName = "runtime.duplicate",
                Group = "runtime.group",
                DuplicateBehavior = RuntimeSubscriptionDuplicateBehavior.Replace,
            },
            AbortToken
        );

        cache.GetCandidatesMethodsOfGroupNameGrouped();
        cache.TryGetMessageNameExecutor("runtime.duplicate", "runtime.group", out var descriptor).Should().BeTrue();
        descriptor.Should().NotBeNull();
        descriptor!.HandlerId.Should().Be(replaced.HandlerId);

        await first.DisposeAsync();
        await replaced.DisposeAsync();
    }

    [Fact]
    public async Task should_refresh_wildcard_runtime_matchers_after_replacing_subscription()
    {
        await using var provider = _CreateProvider();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var firstHandler = provider.GetRequiredService<NamedRuntimeHandler>();
        var secondHandler = provider.GetRequiredService<AlternateNamedRuntimeHandler>();

        var first = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            firstHandler.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.*", Group = "runtime.wildcard" },
            AbortToken
        );

        var initialMatch = selector.SelectBestCandidate("runtime.created", selector.SelectCandidates());
        initialMatch.Should().NotBeNull();
        initialMatch!.HandlerId.Should().Be(first.HandlerId);

        var replaced = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            secondHandler.HandleAsync,
            new RuntimeSubscriptionOptions
            {
                MessageName = "runtime.*",
                Group = "runtime.wildcard",
                DuplicateBehavior = RuntimeSubscriptionDuplicateBehavior.Replace,
            },
            AbortToken
        );

        var refreshedMatch = selector.SelectBestCandidate("runtime.created", selector.SelectCandidates());

        refreshedMatch.Should().NotBeNull();
        refreshedMatch!.HandlerId.Should().Be(replaced.HandlerId);

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
                            MessageName = $"runtime.concurrent.{index}",
                            Group = "runtime.concurrent",
                        },
                        AbortToken
                    )
                    .AsTask()
        );

        var handles = await Task.WhenAll(subscribeTasks);
        cache.GetAllMessageNames().Should().HaveCount(handlers.Length);

        await Task.WhenAll(handles.Select(handle => handle.DisposeAsync().AsTask()));

        cache.GetAllMessageNames().Should().BeEmpty();
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

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemory();
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
        )
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlternateNamedRuntimeHandler
    {
        public ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConcurrentRuntimeHandler
    {
        public ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.CompletedTask;
        }
    }
}
