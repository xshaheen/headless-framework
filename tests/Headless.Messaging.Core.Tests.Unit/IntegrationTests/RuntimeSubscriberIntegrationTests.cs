using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.IntegrationTests;

public sealed class RuntimeSubscriberIntegrationTests : TestBase
{
    [Fact]
    public async Task should_execute_runtime_handler_with_scoped_di_and_filters()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IOutboxPublisher>();
        var filter = provider.GetRequiredService<RecordingConsumeFilter>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { Topic = "runtime.integration", Group = "runtime.integration" },
            AbortToken
        );

        await publisher.PublishAsync(
            new RuntimeMessage("first"),
            new PublishOptions { Topic = "runtime.integration" },
            AbortToken
        );

        var consumed = await probe.WaitForMessageAsync(AbortToken);

        consumed.Message.Id.Should().Be("first");
        probe.ScopedDependencyIds.Should().ContainSingle();
        filter.ExecutingCount.Should().Be(1);
        filter.ExecutedCount.Should().Be(1);
        filter.ExceptionCount.Should().Be(0);
    }

    [Fact]
    public async Task should_detach_future_deliveries_but_allow_inflight_runtime_handler_to_finish()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IOutboxPublisher>();
        var probe = provider.GetRequiredService<BlockingRuntimeProbe>();

        var handle = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { Topic = "runtime.blocking", Group = "runtime.blocking" },
            AbortToken
        );

        await publisher.PublishAsync(
            new RuntimeMessage("first"),
            new PublishOptions { Topic = "runtime.blocking" },
            AbortToken
        );

        await probe.WaitUntilStartedAsync(AbortToken);
        await handle.DisposeAsync();

        await publisher.PublishAsync(
            new RuntimeMessage("second"),
            new PublishOptions { Topic = "runtime.blocking" },
            AbortToken
        );

        probe.Release();
        await probe.WaitUntilCompletedAsync(AbortToken);
        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);

        probe.ProcessedMessageIds.Should().ContainSingle().Which.Should().Be("first");
    }

    [Fact]
    public async Task should_restart_consumers_for_runtime_subscription_added_after_consumer_register_is_ready()
    {
        var blocker = new BlockingProcessingServer();
        await using var provider = _CreateProvider(blocker);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IOutboxPublisher>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        var bootstrapTask = bootstrapper.BootstrapAsync(AbortToken);
        await blocker.WaitUntilStartedAsync(AbortToken);
        bootstrapper.IsStarted.Should().BeFalse();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { Topic = "runtime.mid-bootstrap", Group = "runtime.mid-bootstrap" },
            AbortToken
        );

        blocker.Release();
        await bootstrapTask;

        await publisher.PublishAsync(
            new RuntimeMessage("mid-bootstrap"),
            new PublishOptions { Topic = "runtime.mid-bootstrap" },
            AbortToken
        );

        var consumed = await probe.WaitForMessageAsync(AbortToken);
        consumed.Message.Id.Should().Be("mid-bootstrap");
    }

    private async Task<ServiceProvider> _CreateStartedProviderAsync()
    {
        var provider = _CreateProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private ServiceProvider _CreateProvider(IProcessingServer? additionalProcessor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<RecordingConsumeFilter>();
        services.AddSingleton<IConsumeFilter>(sp => sp.GetRequiredService<RecordingConsumeFilter>());
        services.AddScoped<ScopedRuntimeDependency>();
        services.AddSingleton<RecordingRuntimeProbe>();
        services.AddSingleton<BlockingRuntimeProbe>();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
            options.UseConventions(c =>
            {
                c.UseApplicationId("runtime-tests");
                c.UseVersion("v1");
            });
        });

        if (additionalProcessor is not null)
        {
            services.AddSingleton<IProcessingServer>(additionalProcessor);
        }

        return services.BuildServiceProvider();
    }

    private sealed record RuntimeMessage(string Id);

    private sealed class ScopedRuntimeDependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class RecordingConsumeFilter : ConsumeFilter
    {
        public int ExecutingCount { get; private set; }
        public int ExecutedCount { get; private set; }
        public int ExceptionCount { get; private set; }

        public override ValueTask OnSubscribeExecutingAsync(ExecutingContext context)
        {
            ExecutingCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnSubscribeExecutedAsync(ExecutedContext context)
        {
            ExecutedCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnSubscribeExceptionAsync(ExceptionContext context)
        {
            ExceptionCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRuntimeProbe
    {
        private readonly TaskCompletionSource<ConsumeContext<RuntimeMessage>> _messageReceived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ConcurrentQueue<Guid> ScopedDependencyIds { get; } = [];

        public ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            var dependency = services.GetRequiredService<ScopedRuntimeDependency>();
            ScopedDependencyIds.Enqueue(dependency.Id);
            _messageReceived.TrySetResult(context);
            return ValueTask.CompletedTask;
        }

        public async Task<ConsumeContext<RuntimeMessage>> WaitForMessageAsync(CancellationToken cancellationToken)
        {
            return await _messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class BlockingRuntimeProbe
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<string> ProcessedMessageIds { get; } = [];

        public async ValueTask HandleAsync(
            ConsumeContext<RuntimeMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            ProcessedMessageIds.Enqueue(context.Message.Id);
            _completed.TrySetResult();
        }

        public async Task WaitUntilStartedAsync(CancellationToken cancellationToken)
        {
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public async Task WaitUntilCompletedAsync(CancellationToken cancellationToken)
        {
            await _completed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class BlockingProcessingServer : IProcessingServer
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            _started.TrySetResult();
            return new ValueTask(_release.Task.WaitAsync(stoppingToken));
        }

        public async Task WaitUntilStartedAsync(CancellationToken cancellationToken)
        {
            await _started.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            _release.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            _release.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
