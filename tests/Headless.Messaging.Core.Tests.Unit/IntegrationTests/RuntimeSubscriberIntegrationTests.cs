using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.IntegrationTests;

public sealed class RuntimeSubscriberIntegrationTests : TestBase
{
    [Fact]
    public async Task should_execute_runtime_handler_with_scoped_di_and_middleware()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var publisher = provider.GetRequiredService<IBus>();
        var middlewareProbe = provider.GetRequiredService<RecordingConsumeMiddlewareProbe>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.integration", Group = "runtime.integration" },
            AbortToken
        );

        await publisher.PublishAsync(
            new RuntimeMessage("first"),
            new PublishOptions { MessageName = "runtime.integration", DeliveryMode = DeliveryMode.Durable },
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
        var publisher = provider.GetRequiredService<IBus>();
        var probe = provider.GetRequiredService<BlockingRuntimeProbe>();

        var handle = await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.blocking", Group = "runtime.blocking" },
            AbortToken
        );

        await publisher.PublishAsync(
            new RuntimeMessage("first"),
            new PublishOptions { MessageName = "runtime.blocking", DeliveryMode = DeliveryMode.Durable },
            AbortToken
        );

        await probe.WaitUntilStartedAsync(AbortToken);
        await handle.DisposeAsync();

        await publisher.PublishAsync(
            new RuntimeMessage("second"),
            new PublishOptions { MessageName = "runtime.blocking", DeliveryMode = DeliveryMode.Durable },
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
        var publisher = provider.GetRequiredService<IBus>();
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
            new PublishOptions { MessageName = "runtime.mid-bootstrap", DeliveryMode = DeliveryMode.Durable },
            AbortToken
        );

        var consumed = await probe.WaitForMessageAsync(AbortToken);
        consumed.Message.Id.Should().Be("mid-bootstrap");
    }

    [Fact]
    public async Task final_stop_racing_runtime_subscription_does_not_start_replacement_consumer_loop()
    {
        var factory = new RestartRaceConsumerClientFactory(blockInitialListenerShutdown: true);
        await using var provider = _CreateProvider(consumerClientFactory: factory);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var consumerRegister = provider.GetRequiredService<IConsumerRegister>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.race.initial", Group = "runtime.race" },
            AbortToken
        );
        await bootstrapper.BootstrapAsync(AbortToken);
        await factory.WaitUntilInitialListenerStartedAsync(AbortToken);

        var restartTask = runtimeSubscriber
            .SubscribeAsync<RuntimeMessage>(
                probe.HandleAsync,
                new RuntimeSubscriptionOptions { MessageName = "runtime.race.replacement", Group = "runtime.race" },
                AbortToken
            )
            .AsTask();
        await factory.WaitUntilInitialListenerShutdownAsync(AbortToken);

        var finalStopTask = consumerRegister.DisposeAsync().AsTask();
        factory.ReleaseInitialListenerShutdown();

        await Task.WhenAll(restartTask, finalStopTask).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        factory.ListenerCreateCount.Should().Be(1, "final stop won before restart could create a replacement loop");
        factory.MaximumConcurrentListenerCount.Should().Be(1, "restart and final stop must never double-start loops");
    }

    [Fact]
    public async Task final_stop_after_restart_drain_does_not_start_replacement_consumer_loop()
    {
        var factory = new RestartRaceConsumerClientFactory(blockReplacementMetadataCreation: true);
        await using var provider = _CreateProvider(consumerClientFactory: factory);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var consumerRegister = provider.GetRequiredService<IConsumerRegister>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.race.initial", Group = "runtime.race" },
            AbortToken
        );
        await bootstrapper.BootstrapAsync(AbortToken);
        await factory.WaitUntilInitialListenerStartedAsync(AbortToken);

        var restartTask = runtimeSubscriber
            .SubscribeAsync<RuntimeMessage>(
                probe.HandleAsync,
                new RuntimeSubscriptionOptions { MessageName = "runtime.race.replacement", Group = "runtime.race" },
                AbortToken
            )
            .AsTask();
        await factory.WaitUntilReplacementMetadataCreationAsync(AbortToken);

        var finalStopTask = consumerRegister.DisposeAsync().AsTask();
        factory.ReleaseReplacementMetadataCreation();

        await Task.WhenAll(restartTask, finalStopTask).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        factory.ListenerCreateCount.Should().Be(1, "final stop won before replacement consumer creation");
        factory.MaximumConcurrentListenerCount.Should().Be(1, "restart and final stop must never double-start loops");
    }

    [Fact]
    public async Task restart_timeout_does_not_overlap_the_previous_consumer_generation()
    {
        var timeProvider = new FakeTimeProvider();
        var factory = new RestartRaceConsumerClientFactory(blockInitialListenerShutdown: true);
        await using var provider = _CreateProvider(consumerClientFactory: factory, timeProvider: timeProvider);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var probe = provider.GetRequiredService<RecordingRuntimeProbe>();

        await runtimeSubscriber.SubscribeAsync<RuntimeMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { MessageName = "runtime.timeout.initial", Group = "runtime.timeout" },
            AbortToken
        );
        await bootstrapper.BootstrapAsync(AbortToken);
        await factory.WaitUntilInitialListenerStartedAsync(AbortToken);

        var restartTask = runtimeSubscriber
            .SubscribeAsync<RuntimeMessage>(
                probe.HandleAsync,
                new RuntimeSubscriptionOptions
                {
                    MessageName = "runtime.timeout.replacement",
                    Group = "runtime.timeout",
                },
                AbortToken
            )
            .AsTask();
        await factory.WaitUntilInitialListenerShutdownAsync(AbortToken);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await restartTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        factory.ListenerCreateCount.Should().Be(1, "a timed-out old generation must fence replacement startup");
        factory.MaximumConcurrentListenerCount.Should().Be(1);

        factory.ReleaseInitialListenerShutdown();
    }

    private async Task<ServiceProvider> _CreateStartedProviderAsync()
    {
        var provider = _CreateProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private ServiceProvider _CreateProvider(
        IProcessingServer? additionalProcessor = null,
        IConsumerClientFactory? consumerClientFactory = null,
        TimeProvider? timeProvider = null
    )
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

        if (consumerClientFactory is not null)
        {
            services.AddSingleton(consumerClientFactory);
        }

        if (timeProvider is not null)
        {
            services.AddSingleton<TimeProvider>(timeProvider);
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

    private sealed class RestartRaceConsumerClientFactory(
        bool blockInitialListenerShutdown = false,
        bool blockReplacementMetadataCreation = false
    ) : IConsumerClientFactory
    {
        private readonly TaskCompletionSource _initialListenerStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _initialListenerShutdown = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _releaseInitialListenerShutdown = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _replacementMetadataCreation = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _releaseReplacementMetadataCreation = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _activeListenerCount;
        private int _createCount;
        private int _listenerCreateCount;
        private int _maximumConcurrentListenerCount;

        public int ListenerCreateCount => Volatile.Read(ref _listenerCreateCount);

        public int MaximumConcurrentListenerCount => Volatile.Read(ref _maximumConcurrentListenerCount);

        public async Task<IConsumerClient> CreateAsync(
            string groupName,
            byte groupConcurrent,
            MessageLane lane,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var createCount = Interlocked.Increment(ref _createCount);

            if ((createCount & 1) == 1)
            {
                if (createCount == 3 && blockReplacementMetadataCreation)
                {
                    _replacementMetadataCreation.TrySetResult();
                    await _releaseReplacementMetadataCreation.Task.WaitAsync(cancellationToken);
                }

                return new MetadataConsumerClient();
            }

            var listenerOrdinal = Interlocked.Increment(ref _listenerCreateCount);
            return new ListenerConsumerClient(this, listenerOrdinal == 1 && blockInitialListenerShutdown);
        }

        public Task WaitUntilInitialListenerStartedAsync(CancellationToken cancellationToken)
        {
            return _initialListenerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public Task WaitUntilInitialListenerShutdownAsync(CancellationToken cancellationToken)
        {
            return _initialListenerShutdown.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public void ReleaseInitialListenerShutdown()
        {
            _releaseInitialListenerShutdown.TrySetResult();
        }

        public Task WaitUntilReplacementMetadataCreationAsync(CancellationToken cancellationToken)
        {
            return _replacementMetadataCreation.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public void ReleaseReplacementMetadataCreation()
        {
            _releaseReplacementMetadataCreation.TrySetResult();
        }

        private void _ListenerStarted()
        {
            var active = Interlocked.Increment(ref _activeListenerCount);
            _UpdateMaximum(active);
            _initialListenerStarted.TrySetResult();
        }

        private void _ListenerStopped()
        {
            Interlocked.Decrement(ref _activeListenerCount);
        }

        private ValueTask _ShutdownAsync(bool block)
        {
            if (!block)
            {
                return ValueTask.CompletedTask;
            }

            _initialListenerShutdown.TrySetResult();
            return new ValueTask(_releaseInitialListenerShutdown.Task);
        }

        private void _UpdateMaximum(int active)
        {
            var observed = Volatile.Read(ref _maximumConcurrentListenerCount);
            while (active > observed)
            {
                observed = Interlocked.CompareExchange(ref _maximumConcurrentListenerCount, active, observed);
            }
        }

        private sealed class MetadataConsumerClient : ConsumerClientBase
        {
            public override ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }
        }

        private sealed class ListenerConsumerClient(RestartRaceConsumerClientFactory owner, bool blockShutdown)
            : ConsumerClientBase
        {
            public override async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
            {
                owner._ListenerStarted();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    owner._ListenerStopped();
                }
            }

            public override ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                return owner._ShutdownAsync(blockShutdown);
            }
        }

        private abstract class ConsumerClientBase : IConsumerClient
        {
            public BrokerAddress BrokerAddress => new("test", "restart-race");

            public Func<TransportMessage, object?, Task>? OnMessageCallback { get; private set; }

            public Action<LogMessageEventArgs>? OnLogCallback { get; private set; }

            public void AttachCallbacks(
                Func<TransportMessage, object?, Task>? onMessage,
                Action<LogMessageEventArgs>? onLog
            )
            {
                OnMessageCallback = onMessage;
                OnLogCallback = onLog;
            }

            public ValueTask<ICollection<string>> FetchMessageNamesAsync(
                IEnumerable<string> messageNames,
                CancellationToken cancellationToken = default
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<ICollection<string>>(messageNames.ToArray());
            }

            public ValueTask SubscribeAsync(
                IEnumerable<string> messageNames,
                CancellationToken cancellationToken = default
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }

            public abstract ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken);

            public virtual ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask PauseAsync(CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask ResumeAsync(CancellationToken cancellationToken = default)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
