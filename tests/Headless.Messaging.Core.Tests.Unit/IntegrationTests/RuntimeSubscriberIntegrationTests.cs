using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.IntegrationTests;

public sealed class RuntimeSubscriberIntegrationTests : TestBase
{
    [Fact]
    public async Task should_execute_runtime_handler_with_scoped_di_and_middleware()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IOutboxBus>();
        var middlewareProbe = provider.GetRequiredService<RecordingConsumeMiddlewareProbe>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.integration", Group = "runtime.integration" },
            AbortToken
        );

        await publisher.PublishAsync(
            new RuntimeMessage("first"),
            new PublishOptions { MessageName = "runtime.integration" },
            AbortToken
        );

        var consumed = await probe.WaitForMessageAsync(AbortToken);
        await middlewareProbe.WaitUntilExecutedAsync(AbortToken);

        consumed.Message.Id.Should().Be("first");
        consumed.MessageName.Should().Be("runtime.integration");
        probe.ScopedDependencyIds.Should().ContainSingle();
        middlewareProbe.ExecutingCount.Should().Be(1);
        middlewareProbe.ExecutedCount.Should().Be(1);
        middlewareProbe.ExceptionCount.Should().Be(0);
    }

    [Fact]
    public async Task should_detach_future_deliveries_but_allow_inflight_runtime_handler_to_finish()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IOutboxBus>();
        var probe = provider.GetRequiredService<BlockingRuntimeProbe>();

        var handle = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.blocking", Group = "runtime.blocking" },
            AbortToken
        );

        await publisher.PublishAsync(
            new RuntimeMessage("first"),
            new PublishOptions { MessageName = "runtime.blocking" },
            AbortToken
        );

        await probe.WaitUntilStartedAsync(AbortToken);
        await handle.DisposeAsync();

        await publisher.PublishAsync(
            new RuntimeMessage("second"),
            new PublishOptions { MessageName = "runtime.blocking" },
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
        await using var blocker = new BlockingProcessingServer();
        await using var provider = _CreateProvider(blocker);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IOutboxBus>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        var bootstrapTask = bootstrapper.BootstrapAsync(AbortToken);
        await blocker.WaitUntilStartedAsync(AbortToken);
        bootstrapper.IsStarted.Should().BeFalse();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.mid-bootstrap", Group = "runtime.mid-bootstrap" },
            AbortToken
        );

        blocker.Release();
        await bootstrapTask;

        await publisher.PublishAsync(
            new RuntimeMessage("mid-bootstrap"),
            new PublishOptions { MessageName = "runtime.mid-bootstrap" },
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

        services.AddSingleton<RecordingConsumeMiddlewareProbe>();
        services.AddScoped<ScopedRuntimeDependency>();
        services.AddSingleton<RecordingRuntimeProbe>();
        services.AddSingleton<BlockingRuntimeProbe>();

        services
            .AddHeadlessMessaging(options =>
            {
                options.UseInMemory();
                options.UseInMemoryStorage();
                options.UseConventions(c =>
                {
                    c.UseApplicationId("runtime-tests");
                    c.UseVersion("v1");
                });
            })
            .AddBusConsumeMiddleware<RecordingConsumeMiddleware>();

        if (additionalProcessor is not null)
        {
            services.AddSingleton(additionalProcessor);
        }

        return services.BuildServiceProvider();
    }

    private sealed record RuntimeMessage(string Id);

    private sealed class ScopedRuntimeDependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class RecordingConsumeMiddleware(RecordingConsumeMiddlewareProbe probe)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            probe.RecordExecuting();

            try
            {
                await next().ConfigureAwait(false);
                probe.RecordExecuted();
            }
            catch
            {
                probe.RecordException();
                throw;
            }
        }
    }

    private sealed class RecordingConsumeMiddlewareProbe
    {
        private readonly TaskCompletionSource _executed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executingCount;
        private int _executedCount;
        private int _exceptionCount;

        public int ExecutingCount => Volatile.Read(ref _executingCount);
        public int ExecutedCount => Volatile.Read(ref _executedCount);
        public int ExceptionCount => Volatile.Read(ref _exceptionCount);

        public void RecordExecuting()
        {
            Interlocked.Increment(ref _executingCount);
        }

        public void RecordExecuted()
        {
            Interlocked.Increment(ref _executedCount);
            _executed.TrySetResult();
        }

        public void RecordException()
        {
            Interlocked.Increment(ref _exceptionCount);
        }

        public Task WaitUntilExecutedAsync(CancellationToken cancellationToken)
        {
            return _executed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
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
            Argument.IsNotNull(services);
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

        public void Release()
        {
            _release.TrySetResult();
        }
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
