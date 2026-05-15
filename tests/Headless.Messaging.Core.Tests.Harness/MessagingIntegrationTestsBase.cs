// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Fixtures;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Base class for full pub-sub cycle integration tests with DI setup.
/// Provides complete messaging infrastructure including transport and storage.
/// </summary>
/// <remarks>
/// <para>
/// This base class sets up a complete messaging system for integration tests:
/// <list type="bullet">
/// <item><description>ServiceCollection with logging and messaging services</description></item>
/// <item><description>Abstract methods for transport and storage configuration</description></item>
/// <item><description>Full pub-sub lifecycle test scenarios</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Usage:</strong>
/// <code>
/// public class RabbitMqIntegrationTests : MessagingIntegrationTestsBase
/// {
///     protected override void ConfigureTransport(MessagingSetupBuilder setup)
///     {
///         setup.UseRabbitMQ(r => r.HostName = "localhost");
///     }
///
///     protected override void ConfigureStorage(MessagingSetupBuilder setup)
///     {
///         setup.UsePostgreSql("connection-string");
///     }
/// }
/// </code>
/// </para>
/// </remarks>
[PublicAPI]
public abstract class MessagingIntegrationTestsBase : TestBase
{
    private ServiceProvider? _serviceProvider;
    private bool _disposed;

    /// <summary>Gets the configured service provider.</summary>
    protected ServiceProvider ServiceProvider =>
        _serviceProvider
        ?? throw new InvalidOperationException(
            "ServiceProvider not initialized. Ensure InitializeAsync has completed."
        );

    /// <summary>Gets the bootstrapper for starting the messaging system.</summary>
    protected IBootstrapper Bootstrapper => ServiceProvider.GetRequiredService<IBootstrapper>();

    /// <summary>Gets the publisher for sending messages.</summary>
    protected IOutboxPublisher Publisher => ServiceProvider.GetRequiredService<IOutboxPublisher>();

    /// <summary>Gets the scheduler-aware publisher for delayed messages.</summary>
    protected IScheduledPublisher ScheduledPublisher => ServiceProvider.GetRequiredService<IScheduledPublisher>();

    /// <summary>Gets the data storage for message persistence.</summary>
    protected IDataStorage DataStorage => ServiceProvider.GetRequiredService<IDataStorage>();

    /// <summary>Gets the consumer registry for discovering registered consumers.</summary>
    protected IConsumerRegistry ConsumerRegistry => ServiceProvider.GetRequiredService<IConsumerRegistry>();

    /// <summary>Gets the direct publisher for transport-only readiness probes.</summary>
    protected IDirectPublisher DirectPublisher => ServiceProvider.GetRequiredService<IDirectPublisher>();

    /// <summary>Gets the resolved messaging options for prefix-aware assertions.</summary>
    protected MessagingOptions MessagingOptions =>
        ServiceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;

    /// <summary>Configures the transport (e.g., RabbitMQ, Kafka, InMemory) for the messaging system.</summary>
    /// <param name="setup">The messaging setup builder to configure.</param>
    protected abstract void ConfigureTransport(MessagingSetupBuilder setup);

    /// <summary>Configures the storage (e.g., PostgreSQL, SqlServer, InMemory) for the messaging system.</summary>
    /// <param name="setup">The messaging setup builder to configure.</param>
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    /// <summary>Optional hook for additional service configuration.</summary>
    /// <param name="services">The service collection to configure.</param>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Optional hook for additional messaging options configuration.</summary>
    /// <param name="setup">The messaging setup builder to configure.</param>
    protected virtual void ConfigureMessaging(MessagingSetupBuilder setup) { }

    /// <inheritdoc />
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Add messaging with abstract configuration
        services.AddHeadlessMessaging(setup =>
        {
            ConfigureTransport(setup);
            ConfigureStorage(setup);
            ConfigureMessaging(setup);

            // Register test consumer
            setup.Subscribe<TestSubscriber>("test-message").Group("test-group").Concurrency(1);

            // Register failing consumer for exception tests
            setup.Subscribe<FailingTestSubscriber>("failing-message").Group("test-group").Concurrency(1);
        });

        // Register test helpers as singletons (same instance used throughout tests)
        services.AddSingleton<TestSubscriber>();
        services.AddSingleton<FailingTestSubscriber>();

        // Allow additional service configuration
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        // Bootstrap the messaging system
        await Bootstrapper.BootstrapAsync(AbortToken);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
            _serviceProvider = null;
        }

        _disposed = true;
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_publish_and_consume_message_end_to_end()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage
        {
            Id = Guid.NewGuid().ToString(),
            Name = "EndToEndTest",
            Payload = "Integration test payload",
        };

        // when
        await Publisher.PublishAsync(message, new PublishOptions { Topic = "test-message" }, AbortToken);

        // Allow time for message processing
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("message should be received within timeout");
        subscriber.ReceivedMessages.Should().ContainSingle(m => m.Id == message.Id);
    }

    public virtual async Task should_discover_consumers_from_di()
    {
        // given, when
        var registeredConsumers = ConsumerRegistry.GetAll();

        // then
        registeredConsumers.Should().NotBeEmpty("consumers should be registered");
        registeredConsumers.Should().Contain(m => m.ConsumerType == typeof(TestSubscriber));
    }

    public virtual async Task should_invoke_consumer_handler_on_message()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "HandlerInvocationTest" };

        // when
        await Publisher.PublishAsync(message, new PublishOptions { Topic = "test-message" }, AbortToken);
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("consumer handler should be invoked");
        subscriber.ReceivedContexts.Should().HaveCountGreaterThanOrEqualTo(1);

        var context = subscriber.ReceivedContexts.First(c => c.Message.Id == message.Id);
        context.MessageId.Should().NotBeNullOrEmpty();
        context.Topic.Should().Be(ResolveTopicName("test-message"));
    }

    public virtual async Task should_store_received_message_in_storage()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "StorageTest" };

        // when
        await Publisher.PublishAsync(message, new PublishOptions { Topic = "test-message" }, AbortToken);
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("message should be received before checking storage");
        var monitoringApi = DataStorage.GetMonitoringApi();
        monitoringApi.Should().NotBeNull("data storage should provide monitoring API");
    }

    public virtual async Task should_handle_consumer_exception()
    {
        // given
        var failingSubscriber = ServiceProvider.GetRequiredService<FailingTestSubscriber>();
        failingSubscriber.Reset();

        var message = new FailingTestMessage { Id = Guid.NewGuid().ToString(), Name = "FailingTest" };

        // when
        await Publisher.PublishAsync(message, new PublishOptions { Topic = "failing-message" }, AbortToken);
        var attempted = await failingSubscriber.WaitForAttemptAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken: AbortToken
        );

        // then
        attempted.Should().BeTrue("consumer exception should be recorded within timeout");
        failingSubscriber.FailedAttempts.Should().BeGreaterThanOrEqualTo(1);
    }

    public virtual async Task should_process_multiple_messages_concurrently()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        const int messageCount = 10;
        var messages = Enumerable
            .Range(0, messageCount)
            .Select(i => new TestMessage { Id = $"concurrent-{i}", Name = $"ConcurrentTest-{i}" })
            .ToList();

        // when
        var publishTasks = messages.Select(m =>
            Publisher.PublishAsync(m, new PublishOptions { Topic = "test-message" }, AbortToken)
        );
        await Task.WhenAll(publishTasks);

        var allReceived = await subscriber.WaitForCountAsync(messageCount, TimeSpan.FromSeconds(30), AbortToken);

        // then
        allReceived.Should().BeTrue($"all {messageCount} messages should be received within timeout");
        subscriber.ReceivedMessages.Should().HaveCount(messageCount);
    }

    public virtual async Task should_retry_failed_message()
    {
        // given
        var failingSubscriber = ServiceProvider.GetRequiredService<FailingTestSubscriber>();
        failingSubscriber.Reset();

        var message = new FailingTestMessage { Id = Guid.NewGuid().ToString(), Name = "RetryTest" };

        // when — wait for at least 2 attempts to prove the message was actually retried
        await Publisher.PublishAsync(message, new PublishOptions { Topic = "failing-message" }, AbortToken);
        var retried = await failingSubscriber.WaitForAttemptAsync(
            TimeSpan.FromSeconds(30),
            minAttempts: 2,
            cancellationToken: AbortToken
        );

        // then
        retried.Should().BeTrue("message should be retried at least once after initial failure");
        failingSubscriber.FailedAttempts.Should().BeGreaterThanOrEqualTo(2);
    }

    public virtual async Task should_complete_message_lifecycle()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "LifecycleTest" };

        // when - publish -> consume -> store -> ack
        await Publisher.PublishAsync(message, new PublishOptions { Topic = "test-message" }, AbortToken);

        // Wait for message to complete lifecycle
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("message should complete full lifecycle");
        subscriber.ReceivedMessages.Should().ContainSingle(m => m.Id == message.Id);

        // Verify message was stored
        var monitoringApi = DataStorage.GetMonitoringApi();
        monitoringApi.Should().NotBeNull();
    }

    public virtual async Task should_publish_message_with_headers()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "HeadersTest" };

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "CustomHeader", "CustomValue" },
            { "TenantId", "test-tenant" },
        };

        // when
        await Publisher.PublishAsync(
            message,
            new PublishOptions { Topic = "test-message", Headers = headers },
            AbortToken
        );
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("message with headers should be received");
        var context = subscriber.ReceivedContexts.FirstOrDefault(c => c.Message.Id == message.Id);
        context.Should().NotBeNull();
        context!.Headers.Should().ContainKey("CustomHeader");
    }

    public virtual async Task should_publish_delayed_message()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "DelayedTest" };

        var startTime = DateTimeOffset.UtcNow;

        // when
        await ScheduledPublisher.PublishDelayAsync(
            TimeSpan.FromSeconds(2),
            message,
            new PublishOptions { Topic = "test-message" },
            AbortToken
        );

        // Message should not be received immediately
        var immediateCheck = await subscriber.WaitForMessageAsync(TimeSpan.FromMilliseconds(500), AbortToken);
        immediateCheck.Should().BeFalse("delayed message should not arrive immediately");

        // Wait for the delayed message
        var delayedCheck = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        delayedCheck.Should().BeTrue("delayed message should eventually arrive");
        var elapsed = DateTimeOffset.UtcNow - startTime;
        elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(1), "message should be delayed");
    }

    public virtual Task should_bootstrap_messaging_system()
    {
        // given, when
        var isStarted = Bootstrapper.IsStarted;

        // then
        isStarted.Should().BeTrue("bootstrapper should be started after initialization");
        return Task.CompletedTask;
    }

    protected async Task EnsureTestSubscriberReadyAsync(
        string topic = "test-message",
        TimeSpan? timeout = null,
        TimeSpan? retryInterval = null
    )
    {
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        var pause = retryInterval ?? TimeSpan.FromMilliseconds(250);
        var lastMessageId = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            subscriber.Clear();

            var probe = new TestMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "ReadinessProbe",
                Payload = "probe",
            };

            lastMessageId = probe.Id;

            await DirectPublisher.PublishAsync(probe, new PublishOptions { Topic = topic }, AbortToken);

            var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(1), AbortToken);
            if (received && subscriber.ReceivedMessages.Any(message => message.Id == probe.Id))
            {
                subscriber.Clear();
                return;
            }

            await Task.Delay(pause, AbortToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for test subscriber readiness on topic '{topic}'. Last probe id: '{lastMessageId}'."
        );
    }

    protected string ResolveTopicName(string topic)
    {
        return string.IsNullOrWhiteSpace(MessagingOptions.TopicNamePrefix)
            ? topic
            : string.Concat(MessagingOptions.TopicNamePrefix, ".", topic);
    }
}

/// <summary>Test subscriber that always throws to test exception handling and retries.</summary>
[PublicAPI]
public sealed class FailingTestSubscriber : IConsume<FailingTestMessage>
{
    private readonly Lock _lock = new();
    private int _failedAttempts;
    private TaskCompletionSource<bool> _attemptTcs = new();

    /// <summary>Gets the number of failed processing attempts.</summary>
    /// <remarks>
    /// Volatile.Read provides acquire semantics for lock-free reads from outside the lock.
    /// Writes use plain increment/assignment inside <see cref="_lock"/> whose release barrier publishes them.
    /// </remarks>
    public int FailedAttempts => Volatile.Read(ref _failedAttempts);

    public ValueTask Consume(ConsumeContext<FailingTestMessage> context, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _failedAttempts++;
            _attemptTcs.TrySetResult(true);
            _attemptTcs = new TaskCompletionSource<bool>();
        }

        throw new InvalidOperationException($"Simulated failure for message {context.MessageId}");
    }

    /// <summary>Resets the failed attempt counter and signal atomically.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _failedAttempts = 0;
            _attemptTcs = new TaskCompletionSource<bool>();
        }
    }

    /// <summary>Waits until at least <paramref name="minAttempts"/> failure attempts are recorded, or timeout.</summary>
    public async Task<bool> WaitForAttemptAsync(
        TimeSpan timeout,
        int minAttempts = 1,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                Task waitTask;

                lock (_lock)
                {
                    if (_failedAttempts >= minAttempts)
                    {
                        return true;
                    }

                    waitTask = _attemptTcs.Task;
                }

                await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
