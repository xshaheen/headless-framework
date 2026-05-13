using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class ConsumerRegisterTests : TestBase
{
    [Fact]
    public async Task restart_keeps_consumer_shutdown_linked_to_the_host_token()
    {
        await using var provider = _CreateProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        using var hostCts = new CancellationTokenSource();

        await register.StartAsync(hostCts.Token);
        await register.ReStartAsync(force: true);

        await hostCts.CancelAsync();

        var field = typeof(ConsumerRegister).GetField(
            "_stoppingCts",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        var linkedCts = (CancellationTokenSource)field!.GetValue(register)!;

        linkedCts.IsCancellationRequested.Should().BeTrue();

        await register.DisposeAsync();
    }

    [Fact]
    public async Task resume_group_async_propagates_resume_failures_after_logging_them()
    {
        await using var provider = _CreateProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        var client = Substitute.For<IConsumerClient>();
        var expected = new InvalidOperationException("resume failed");

        client.ResumeAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromException(expected));

        var handleType = typeof(ConsumerRegister).GetNestedType("GroupHandle", BindingFlags.NonPublic)!;
        var handle = Activator.CreateInstance(handleType, nonPublic: true)!;

        handleType.GetProperty("Logger")!.SetValue(handle, NullLogger<ConsumerRegister>.Instance);
        handleType.GetProperty("Cts")!.SetValue(handle, new CancellationTokenSource());
        handleType.GetProperty("GroupName")!.SetValue(handle, "payments");
        handleType.GetProperty("ConsumerTasks")!.SetValue(handle, new ConcurrentBag<Task>());

        var addClient = handleType.GetMethod("AddClientAsync")!;
        await ((ValueTask)addClient.Invoke(handle, [client])!);

        var resumeGroup = typeof(ConsumerRegister).GetMethod(
            "_ResumeGroupAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var act = async () => await ((ValueTask)resumeGroup.Invoke(register, [handle])!);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("resume failed");
    }

    [Fact]
    public async Task restart_disposes_cts_when_execute_async_throws()
    {
        await using var provider = _CreateProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        using var hostCts = new CancellationTokenSource();

        // Start normally so internal fields are initialized.
        await register.StartAsync(hostCts.Token);

        // Swap _selector with a MethodMatcherCache whose _entries are pre-populated so
        // ExecuteAsync enters the foreach loop and calls the factory.
        var selectorSub = Substitute.For<IConsumerServiceSelector>();
        var fakeCache = new MethodMatcherCache(selectorSub);
        var entriesField = typeof(MethodMatcherCache).GetField(
            "_entries",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var fakeEntries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
            StringComparer.Ordinal
        );
        var fakeDescriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = typeof(object).GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                Type.EmptyTypes
            )!,
            ImplTypeInfo = typeof(object).GetTypeInfo(),
            TopicName = "fake-topic",
            GroupName = "fake-group",
        };
        fakeEntries.TryAdd("fake-group", [fakeDescriptor]);
        entriesField.SetValue(fakeCache, fakeEntries);

        typeof(ConsumerRegister)
            .GetField("_selector", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(register, fakeCache);

        // Swap _consumerClientFactory with one that throws a non-BrokerConnectionException
        // so the exception propagates out of ExecuteAsync.
        var factorySub = Substitute.For<IConsumerClientFactory>();
        factorySub
            .CreateAsync(Arg.Any<string>(), Arg.Any<byte>())
            .Returns<Task<IConsumerClient>>(_ => throw new InvalidOperationException("boom"));

        typeof(ConsumerRegister)
            .GetField(
                "_consumerClientFactory",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(register, factorySub);

        // Act — ReStartAsync should propagate the exception from ExecuteAsync.
        var act = async () => await register.ReStartAsync(force: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        // Assert — the CTS created inside ReStartAsync must have been disposed.
        var ctsField = typeof(ConsumerRegister).GetField(
            "_stoppingCts",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var cts = (CancellationTokenSource)ctsField.GetValue(register)!;
        var tokenAccess = () => cts.Token;
        tokenAccess.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task restart_normalizes_halfopen_circuit_and_pauses_new_transport()
    {
        // given — a mock circuit breaker that reports Open for a group
        var mockCb = Substitute.For<ICircuitBreakerStateManager>();
        const string groupName = "fake-group";

        mockCb.IsOpen(groupName).Returns(true);
        mockCb.AbortHalfOpenProbeAsync(groupName).Returns(ValueTask.CompletedTask);
        mockCb.RemoveGroupAsync(Arg.Any<string>()).Returns(ValueTask.CompletedTask);
        mockCb.RegisterKnownGroups(Arg.Any<IEnumerable<string>>());

        await using var provider = _CreateProvider(mockCb);
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        using var hostCts = new CancellationTokenSource();

        // Start normally — in-memory queue has no subscribers so ExecuteAsync is a no-op
        await register.StartAsync(hostCts.Token);

        // Inject mock CB into the field (StartAsync resolves ICircuitBreakerStateManager via GetService)
        var cbField = typeof(ConsumerRegister).GetField(
            "_circuitBreakerStateManager",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        cbField.SetValue(register, mockCb);

        // Swap selector with a fake that has one group so ExecuteAsync enters the per-group loop
        var selectorSub = Substitute.For<IConsumerServiceSelector>();
        var fakeCache = new MethodMatcherCache(selectorSub);
        var entriesField = typeof(MethodMatcherCache).GetField(
            "_entries",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var fakeEntries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
            StringComparer.Ordinal
        );
        var fakeDescriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = typeof(object).GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                Type.EmptyTypes
            )!,
            ImplTypeInfo = typeof(object).GetTypeInfo(),
            TopicName = "fake-topic",
            GroupName = groupName,
        };
        fakeEntries.TryAdd(groupName, [fakeDescriptor]);
        entriesField.SetValue(fakeCache, fakeEntries);
        typeof(ConsumerRegister)
            .GetField("_selector", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(register, fakeCache);

        // Swap factory: first call returns a metadata client; per-thread calls return a
        // ready listener so ExecuteAsync can finish and expose the paused handle state.
        var readyClient = new ReadyListeningConsumerClient();
        var callCount = 0;
        var factorySub = Substitute.For<IConsumerClientFactory>();
        factorySub
            .CreateAsync(Arg.Any<string>(), Arg.Any<byte>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    var client = Substitute.For<IConsumerClient>();
                    client
                        .FetchTopicsAsync(Arg.Any<IEnumerable<string>>())
                        .Returns(ValueTask.FromResult<ICollection<string>>(["fake-topic"]));
                    return Task.FromResult(client);
                }

                return Task.FromResult<IConsumerClient>(readyClient);
            });
        typeof(ConsumerRegister)
            .GetField(
                "_consumerClientFactory",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .SetValue(register, factorySub);

        // when — call ExecuteAsync directly (the internal path ReStartAsync uses after PulseAsync)
        var executeAsync = typeof(ConsumerRegister).GetMethod(
            "ExecuteAsync",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            Type.EmptyTypes,
            modifiers: null
        )!;
        await (ValueTask)executeAsync.Invoke(register, null)!;

        // then — AbortHalfOpenProbeAsync was called for the group
        await mockCb.Received(1).AbortHalfOpenProbeAsync(groupName);

        // then — the handle for the group has IsPaused = true because IsOpen returned true
        var groupHandlesField = typeof(ConsumerRegister).GetField(
            "_groupHandles",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var handles = groupHandlesField.GetValue(register)!;
        // Use dynamic to avoid compile-time knowledge of GroupHandle's private nested type
        var handleType = handles.GetType().GetGenericArguments()[1]; // ConcurrentDictionary<string, GroupHandle>
        var tryGetMethod = handles.GetType().GetMethod("TryGetValue")!;
        var handleArgs = new object?[] { groupName, null };
        var found = (bool)tryGetMethod.Invoke(handles, handleArgs)!;
        found.Should().BeTrue("handle should have been created for the group");
        var handleObj = handleArgs[1]!;
        var isPausedProp = handleType.GetProperty("IsPaused", BindingFlags.Public | BindingFlags.Instance)!;
        var isPaused = (bool)isPausedProp.GetValue(handleObj)!;
        isPaused.Should().BeTrue("handle should be pre-paused when circuit IsOpen returns true");

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_queue_topology_refresh_when_change_occurs_during_startup()
    {
        await using var provider = _CreateProvider();
        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();

        // Set state to Starting (1) to simulate mid-startup.
        typeof(ConsumerRegister)
            .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(register, 1);

        await register.OnTopologyChangedAsync();

        var pendingRefresh = typeof(ConsumerRegister)
            .GetField(
                "_pendingTopologyRefresh",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetValue(register);

        pendingRefresh.Should().Be(1);
    }

    [Fact]
    public async Task should_not_complete_startup_until_consumer_listener_is_ready()
    {
        var startupClient = new StartupControlledConsumerClient();
        var factory = new SequencedConsumerClientFactory(new MetadataConsumerClient(), startupClient);

        await using var provider = _CreateProvider(
            configureMessaging: options =>
            {
                options.Subscribe<BootstrapReadyConsumer>("ready-topic").Group("ready-group").Concurrency(1);
            },
            configureServices: services =>
            {
                services.AddSingleton<IConsumerClientFactory>(factory);
                services.AddSingleton<BootstrapReadyConsumer>();
            }
        );

        var register = (ConsumerRegister)provider.GetRequiredService<IConsumerRegister>();
        using var hostCts = new CancellationTokenSource();

        var startTask = register.StartAsync(hostCts.Token).AsTask();
        await startupClient.WaitUntilListeningEnteredAsync(AbortToken);

        startTask.IsCompleted.Should().BeFalse("bootstrap readiness should wait for the consumer listener");

        startupClient.SignalReady();
        await startTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        await register.DisposeAsync();
    }

    private ServiceProvider _CreateProvider(
        ICircuitBreakerStateManager? circuitBreakerStateManager = null,
        Action<MessagingOptions>? configureMessaging = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
            options.UseConventions(c =>
            {
                c.UseApplicationId("messaging-tests");
                c.UseVersion("v1");
            });

            configureMessaging?.Invoke(options);
        });

        if (circuitBreakerStateManager is not null)
        {
            services.AddSingleton(circuitBreakerStateManager);
        }

        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class BootstrapReadyConsumer : IConsume<BootstrapReadyMessage>
    {
        public ValueTask Consume(ConsumeContext<BootstrapReadyMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record BootstrapReadyMessage;

    private sealed class SequencedConsumerClientFactory(params IConsumerClient[] clients) : IConsumerClientFactory
    {
        private readonly Queue<IConsumerClient> _clients = new(clients);

        public Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent)
        {
            if (_clients.Count == 0)
            {
                throw new InvalidOperationException("No consumer clients left in the factory sequence.");
            }

            return Task.FromResult(_clients.Dequeue());
        }
    }

    private sealed class MetadataConsumerClient : IConsumerClient
    {
        public BrokerAddress BrokerAddress => new("test", "metadata");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
        {
            return ValueTask.FromResult<ICollection<string>>(topicNames.ToArray());
        }

        public ValueTask SubscribeAsync(IEnumerable<string> topics) => ValueTask.CompletedTask;

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask RejectAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask PauseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ResumeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StartupControlledConsumerClient : IConsumerClient
    {
        private readonly TaskCompletionSource _listeningEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BrokerAddress BrokerAddress => new("test", "startup");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
        {
            return ValueTask.FromResult<ICollection<string>>(topicNames.ToArray());
        }

        public ValueTask SubscribeAsync(IEnumerable<string> topics) => ValueTask.CompletedTask;

        public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            _listeningEntered.TrySetResult();
            await _ready.Task.WaitAsync(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public async Task WaitUntilListeningEnteredAsync(CancellationToken cancellationToken)
        {
            await _listeningEntered.Task.WaitAsync(cancellationToken);
        }

        public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
        }

        public void SignalReady()
        {
            _ready.TrySetResult();
        }

        public ValueTask CommitAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask RejectAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask PauseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ResumeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _ready.TrySetCanceled();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReadyListeningConsumerClient : IConsumerClient
    {
        public BrokerAddress BrokerAddress => new("test", "ready");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public ValueTask<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames)
        {
            return ValueTask.FromResult<ICollection<string>>(topicNames.ToArray());
        }

        public ValueTask SubscribeAsync(IEnumerable<string> topics) => ValueTask.CompletedTask;

        public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask CommitAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask RejectAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask PauseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ResumeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
