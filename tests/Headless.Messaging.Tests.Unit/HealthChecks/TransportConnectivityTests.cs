// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.HealthChecks;

public sealed class TransportConnectivityTests : TestBase
{
    [Fact]
    public void should_stay_reachable_when_consecutive_broker_failures_stay_below_the_threshold()
    {
        var connectivity = new TransportConnectivity();

        connectivity.ReportBrokerFailure();
        connectivity.ReportBrokerFailure();

        connectivity.IsReachable.Should().BeTrue("one or two failed receives are a blip the client retries through");
    }

    [Fact]
    public void should_turn_unreachable_when_consecutive_broker_failures_reach_the_threshold()
    {
        var connectivity = new TransportConnectivity();

        connectivity.ReportBrokerFailure();
        connectivity.ReportBrokerFailure();
        connectivity.ReportBrokerFailure();

        connectivity.IsReachable.Should().BeFalse();
    }

    [Fact]
    public void should_count_failures_again_from_zero_after_a_success()
    {
        var connectivity = new TransportConnectivity();

        connectivity.ReportBrokerFailure();
        connectivity.ReportBrokerFailure();
        connectivity.ReportBrokerSuccess();
        connectivity.ReportBrokerFailure();
        connectivity.ReportBrokerFailure();

        connectivity.IsReachable.Should().BeTrue("the success broke the run of failures");
    }

    [Fact]
    public void should_turn_reachable_again_on_a_success()
    {
        var connectivity = new TransportConnectivity();
        for (var i = 0; i < 10; i++)
        {
            connectivity.ReportBrokerFailure();
        }

        connectivity.ReportBrokerSuccess();

        connectivity.IsReachable.Should().BeTrue();
    }

    [Theory]
    [InlineData(MqLogType.BrokerUnreachable)]
    [InlineData(MqLogType.ConsumerShutdown)]
    [InlineData(MqLogType.ServerConnError)]
    [InlineData(MqLogType.ConnectError)]
    public async Task should_turn_unreachable_when_a_consumer_client_reports_consecutive_broker_failures(
        MqLogType logType
    )
    {
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        var client = factory.LatestListeningClient;

        client.Report(logType);
        client.Report(logType);
        connectivity.IsReachable.Should().BeTrue();

        client.Report(logType);
        connectivity.IsReachable.Should().BeFalse();

        await register.DisposeAsync();
    }

    [Theory]
    [InlineData(MqLogType.ConsumeError)]
    [InlineData(MqLogType.ExceptionReceived)]
    [InlineData(MqLogType.RedisConsumeError)]
    [InlineData(MqLogType.InvalidIdFormat)]
    [InlineData(MqLogType.MessageNotInflight)]
    [InlineData(MqLogType.AsyncErrorEvent)]
    public async Task should_not_count_message_level_client_reports_as_broker_failures(MqLogType logType)
    {
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        var client = factory.LatestListeningClient;

        for (var i = 0; i < 5; i++)
        {
            client.Report(logType);
        }

        connectivity.IsReachable.Should().BeTrue();

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_turn_reachable_when_the_consumer_client_reports_the_broker_reachable_again()
    {
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        var client = factory.LatestListeningClient;
        client.Report(MqLogType.BrokerUnreachable);
        client.Report(MqLogType.BrokerUnreachable);
        client.Report(MqLogType.BrokerUnreachable);

        client.Report(MqLogType.BrokerReachable);

        connectivity.IsReachable.Should().BeTrue();

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_log_unreachable_only_until_the_run_reaches_the_threshold()
    {
        var factory = new ControlledClientFactory();
        using var logs = new CapturingLoggerProvider();
        await using var provider = _CreateProvider(factory, logs);
        var register = provider.GetRequiredService<IConsumerRegister>();
        await register.StartAsync(AbortToken);
        var client = factory.LatestListeningClient;

        for (var i = 0; i < 10; i++)
        {
            client.Report(MqLogType.BrokerUnreachable);
        }

        logs.Count("TransportBrokerUnreachable").Should().Be(TransportConnectivity.FailureThreshold);

        client.Report(MqLogType.BrokerReachable);
        client.Report(MqLogType.BrokerUnreachable);

        logs.Count("TransportBrokerUnreachable")
            .Should()
            .Be(TransportConnectivity.FailureThreshold + 1, "a success starts a new run, which is logged again");

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_turn_reachable_when_a_message_is_delivered_after_send_failures()
    {
        // given a run of send failures that no consumer client reported
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        for (var i = 0; i < TransportConnectivity.FailureThreshold; i++)
        {
            connectivity.ReportBrokerFailure();
        }

        // when a delivery arrives
        await factory.LatestListeningClient.DeliverAsync();

        // then
        connectivity.IsReachable.Should().BeTrue("a delivered message proves the broker answers");

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_stay_unreachable_across_restarts_while_the_broker_is_down()
    {
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        factory.LatestListeningClient.Report(MqLogType.ConsumerShutdown);
        factory.BrokerDown = true;

        // Each restart resets the register's own restart flag before it reconnects; that optimistic reset must not
        // read as a recovered broker.
        for (var i = 0; i < 4; i++)
        {
            await register.ReStartAsync(force: true, AbortToken);
            if (i >= 1)
            {
                connectivity.IsReachable.Should().BeFalse($"restart {i + 1} could not reach the broker");
            }
        }

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_stay_unreachable_when_a_restarted_client_becomes_ready_without_reaching_the_broker()
    {
        // given a client that, like a Kafka consumer, starts and reports ready offline while it logs connection errors
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        var client = factory.LatestListeningClient;
        client.Report(MqLogType.ServerConnError);
        client.Report(MqLogType.ServerConnError);
        client.Report(MqLogType.ServerConnError);
        factory.FailWhileStarting = MqLogType.ServerConnError;

        // when
        await register.ReStartAsync(force: true, AbortToken);

        // then
        connectivity.IsReachable.Should().BeFalse("the restarted client never reached the broker");

        await register.DisposeAsync();
    }

    [Fact]
    public async Task should_turn_reachable_when_a_restart_establishes_a_consumer_client()
    {
        var factory = new ControlledClientFactory();
        await using var provider = _CreateProvider(factory);
        var register = provider.GetRequiredService<IConsumerRegister>();
        var connectivity = provider.GetRequiredService<TransportConnectivity>();
        await register.StartAsync(AbortToken);
        factory.BrokerDown = true;
        await register.ReStartAsync(force: true, AbortToken);
        await register.ReStartAsync(force: true, AbortToken);
        await register.ReStartAsync(force: true, AbortToken);
        connectivity.IsReachable.Should().BeFalse();

        factory.BrokerDown = false;
        await register.ReStartAsync(force: true, AbortToken);

        connectivity.IsReachable.Should().BeTrue();

        await register.DisposeAsync();
    }

    private ServiceProvider _CreateProvider(ControlledClientFactory factory, ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });
        services.ConfigureMessaging(messaging => messaging.Message<ConnectivityProbe>("connectivity-probe"));
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.AddConsumer<ConnectivityProbeConsumer>();
        });

        // Registered after the transport so the controlled factory replaces the in-memory one.
        services.AddSingleton<IConsumerClientFactory>(factory);
        services.AddSingleton<ConnectivityProbeConsumer>();

        return services.BuildServiceProvider();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _events = new();

        public int Count(string eventName) => _events.Count(x => string.Equals(x, eventName, StringComparison.Ordinal));

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_events);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (eventId.Name is { } name)
                {
                    events.Enqueue(name);
                }
            }
        }
    }

    private sealed record ConnectivityProbe;

    [BusConsumer("tests.transport-connectivity.probe")]
    private sealed class ConnectivityProbeConsumer : IConsume<ConnectivityProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<ConnectivityProbe> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledClientFactory : IConsumerClientFactory
    {
        private readonly ConcurrentQueue<ControlledConsumerClient> _listening = new();

        public volatile bool BrokerDown;

        public MqLogType? FailWhileStarting { get; set; }

        public ControlledConsumerClient LatestListeningClient =>
            _listening.LastOrDefault() ?? throw new InvalidOperationException("No consumer client is listening.");

        public Task<IConsumerClient> CreateAsync(
            ConsumerClientRequest request,
            CancellationToken cancellationToken = default
        )
        {
            if (BrokerDown)
            {
                throw new BrokerConnectionException(new InvalidOperationException("connection refused"));
            }

            var client = new ControlledConsumerClient(_listening, FailWhileStarting);
            return Task.FromResult<IConsumerClient>(client);
        }
    }

    private sealed class ControlledConsumerClient(
        ConcurrentQueue<ControlledConsumerClient> listening,
        MqLogType? failWhileStarting
    ) : IConsumerClient
    {
        public BrokerAddress BrokerAddress => new("test", "controlled");

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public void AttachCallbacks(
            Func<TransportMessage, object?, Task>? onMessage,
            Action<LogMessageEventArgs>? onLog
        )
        {
            OnMessageCallback = onMessage;
            OnLogCallback = onLog;
        }

        public void Report(MqLogType logType)
        {
            OnLogCallback!(new LogMessageEventArgs { LogType = logType, Reason = "test" });
        }

        // Only the arrival matters here, so a message the consumer pipeline later rejects is fine.
        public async Task DeliverAsync()
        {
            var message = new TransportMessage(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = "connectivity-probe",
                },
                ReadOnlyMemory<byte>.Empty
            );

            try
            {
                await OnMessageCallback!(message, arg2: null);
            }
#pragma warning disable CA1031, ERP022 // False positive: the test asserts the arrival's effect, not how the pipeline handles the probe.
            catch (Exception)
            {
                // The probe has no valid body; the pipeline's handling of it is not under test.
            }
#pragma warning restore CA1031, ERP022
        }

        // Only a listening client subscribes, and it does so before the register reports the start complete.
        public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default)
        {
            listening.Enqueue(this);
            if (failWhileStarting is { } logType)
            {
                Report(logType);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown or restart retired this client.
            }
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
