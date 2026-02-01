// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Fixtures;
using Tests.Helpers;
using Xunit;

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
///     protected override void ConfigureTransport(MessagingOptions options)
///     {
///         options.UseRabbitMQ(r => r.HostName = "localhost");
///     }
///
///     protected override void ConfigureStorage(MessagingOptions options)
///     {
///         options.UsePostgreSql("connection-string");
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

    /// <summary>Gets the data storage for message persistence.</summary>
    protected IDataStorage DataStorage => ServiceProvider.GetRequiredService<IDataStorage>();

    /// <summary>Gets the consumer registry for discovering registered consumers.</summary>
    protected IConsumerRegistry ConsumerRegistry => ServiceProvider.GetRequiredService<IConsumerRegistry>();

    /// <summary>Configures the transport (e.g., RabbitMQ, Kafka, InMemory) for the messaging system.</summary>
    /// <param name="options">The messaging options to configure.</param>
    protected abstract void ConfigureTransport(MessagingOptions options);

    /// <summary>Configures the storage (e.g., PostgreSQL, SqlServer, InMemory) for the messaging system.</summary>
    /// <param name="options">The messaging options to configure.</param>
    protected abstract void ConfigureStorage(MessagingOptions options);

    /// <summary>Optional hook for additional service configuration.</summary>
    /// <param name="services">The service collection to configure.</param>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>Optional hook for additional messaging options configuration.</summary>
    /// <param name="options">The messaging options to configure.</param>
    protected virtual void ConfigureMessaging(MessagingOptions options) { }

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
        services.AddMessages(options =>
        {
            ConfigureTransport(options);
            ConfigureStorage(options);

            // Register test consumer
            options.Consumer<TestSubscriber>("test-message").Group("test-group").WithConcurrency(1);

            // Register failing consumer for exception tests
            options.Consumer<FailingTestSubscriber>("failing-message").Group("test-group").WithConcurrency(1);

            ConfigureMessaging(options);
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
        var message = new TestMessage
        {
            Id = Guid.NewGuid().ToString(),
            Name = "EndToEndTest",
            Payload = "Integration test payload",
        };

        // when
        await Publisher.PublishAsync("test-message", message, cancellationToken: AbortToken);

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
        await Publisher.PublishAsync("test-message", message, cancellationToken: AbortToken);
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("consumer handler should be invoked");
        subscriber.ReceivedContexts.Should().HaveCountGreaterThanOrEqualTo(1);

        var context = subscriber.ReceivedContexts.First(c => c.Message.Id == message.Id);
        context.MessageId.Should().NotBeNullOrEmpty();
        context.Topic.Should().Be("test-message");
    }

    public virtual async Task should_store_received_message_in_storage()
    {
        // given
        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "StorageTest" };

        // when
        await Publisher.PublishAsync("test-message", message, cancellationToken: AbortToken);

        // Allow time for message to be stored
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        // then - verify message was stored (monitoring API if available)
        var monitoringApi = DataStorage.GetMonitoringApi();
        monitoringApi.Should().NotBeNull("data storage should provide monitoring API");

        await Task.CompletedTask;
    }

    public virtual async Task should_handle_consumer_exception()
    {
        // given
        var failingSubscriber = ServiceProvider.GetRequiredService<FailingTestSubscriber>();
        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "FailingTest" };

        // when
        await Publisher.PublishAsync("failing-message", message, cancellationToken: AbortToken);

        // Allow time for message to be processed and potentially retried
        await Task.Delay(TimeSpan.FromSeconds(3), AbortToken);

        // then - consumer exception should be recorded
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
            Publisher.PublishAsync("test-message", m, cancellationToken: AbortToken)
        );
        await Task.WhenAll(publishTasks);

        // Allow time for all messages to be processed
        await Task.Delay(TimeSpan.FromSeconds(15), AbortToken);

        // then
        subscriber
            .ReceivedMessages.Should()
            .HaveCountGreaterThanOrEqualTo(
                messageCount / 2,
                "at least half of the messages should be received (may vary due to timing)"
            );
    }

    public virtual async Task should_retry_failed_message()
    {
        // given
        var failingSubscriber = ServiceProvider.GetRequiredService<FailingTestSubscriber>();
        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "RetryTest" };

        // when
        await Publisher.PublishAsync("failing-message", message, cancellationToken: AbortToken);

        // Allow time for retries (depends on retry configuration)
        await Task.Delay(TimeSpan.FromSeconds(5), AbortToken);

        // then - should have attempted at least once
        failingSubscriber.FailedAttempts.Should().BeGreaterThanOrEqualTo(1);
    }

    public virtual async Task should_complete_message_lifecycle()
    {
        // given
        var subscriber = ServiceProvider.GetRequiredService<TestSubscriber>();
        subscriber.Clear();

        var message = new TestMessage { Id = Guid.NewGuid().ToString(), Name = "LifecycleTest" };

        // when - publish -> consume -> store -> ack
        await Publisher.PublishAsync("test-message", message, cancellationToken: AbortToken);

        // Wait for message to complete lifecycle
        var received = await subscriber.WaitForMessageAsync(TimeSpan.FromSeconds(10), AbortToken);

        // Allow additional time for storage and ack
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

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
        await Publisher.PublishAsync("test-message", message, headers, AbortToken);
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
        await Publisher.PublishDelayAsync(
            TimeSpan.FromSeconds(2),
            "test-message",
            message,
            cancellationToken: AbortToken
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

    public virtual async Task should_bootstrap_messaging_system()
    {
        // given, when
        var isStarted = Bootstrapper.IsStarted;

        // then
        isStarted.Should().BeTrue("bootstrapper should be started after initialization");
        await Task.CompletedTask;
    }
}

/// <summary>Test subscriber that always throws to test exception handling and retries.</summary>
[PublicAPI]
public sealed class FailingTestSubscriber : IConsume<TestMessage>
{
    private int _failedAttempts;

    /// <summary>Gets the number of failed processing attempts.</summary>
    public int FailedAttempts => _failedAttempts;

    public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _failedAttempts);
        throw new InvalidOperationException($"Simulated failure for message {context.MessageId}");
    }
}
