// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.IntegrationTests;

/// <summary>
/// Integration tests verifying IDirectPublisher works with real InMemoryQueue transport.
/// These tests verify end-to-end message flow from publishing to consumption.
/// </summary>
/// <remarks>
/// These tests use static state to capture messages because the DI container creates
/// its own consumer instances. This is the standard pattern used across the codebase.
/// </remarks>
public sealed class IDirectPublisherIntegrationTests : TestBase
{
    public override ValueTask InitializeAsync()
    {
        // Reset static state before each test to ensure isolation
        DirectTestConsumer.Reset();
        DirectTestConsumerWithHeaders.Reset();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task should_dispatch_directly_to_consumer_without_transport()
    {
        // given - Test the dispatcher directly to verify consumer registration works
        var services = new ServiceCollection();
        services.AddLogging(x => x.AddProvider(LoggerProvider));
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "test-group";
            messaging.Version = "v1";
            messaging.WithTopicMapping<DirectTestMessage>("direct-test-topic");
            messaging.Consumer<DirectTestConsumer>().Topic("direct-test-topic").Build();
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();

        // Build consume context manually (bypasses transport and deserialization)
        var message = new DirectTestMessage("direct-dispatch-value");
        var context = new ConsumeContext<DirectTestMessage>
        {
            Message = message,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "direct-test-topic",
        };

        // when
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
        await dispatcher.DispatchAsync(context, AbortToken);

        // then
        DirectTestConsumer.ReceivedMessages.Should().HaveCount(1);
        DirectTestConsumer.ReceivedMessages.First().Value.Should().Be("direct-dispatch-value");

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task should_send_message_directly_to_transport_and_receive_by_consumer()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging(x => x.AddProvider(LoggerProvider));
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "test-group";
            messaging.Version = "v1";
            messaging.WithTopicMapping<DirectTestMessage>("direct-test-topic");
            messaging.Consumer<DirectTestConsumer>().Topic("direct-test-topic").Build();
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var directPublisher = scope.ServiceProvider.GetRequiredService<IDirectPublisher>();

        // when
        await directPublisher.PublishAsync(new DirectTestMessage("test-value-123"), AbortToken);

        // Wait for the message to be consumed
        var received = await DirectTestConsumer.WaitForMessageAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        received.Should().BeTrue("Consumer should receive the message");
        DirectTestConsumer.ReceivedMessages.Should().HaveCount(1);
        DirectTestConsumer.ReceivedMessages.First().Value.Should().Be("test-value-123");

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task should_resolve_topic_from_mapping_correctly()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging(x => x.AddProvider(LoggerProvider));
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "test-group";
            messaging.Version = "v1";
            messaging.WithTopicMapping<DirectTestMessage>("custom-topic-name");
            messaging.Consumer<DirectTestConsumer>().Topic("custom-topic-name").Build();
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var directPublisher = scope.ServiceProvider.GetRequiredService<IDirectPublisher>();

        // when - PublishAsync uses the mapped topic, not the type name
        await directPublisher.PublishAsync(new DirectTestMessage("mapping-test"), AbortToken);

        var received = await DirectTestConsumer.WaitForMessageAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        received.Should().BeTrue("Message should be delivered via topic mapping");
        DirectTestConsumer.ReceivedMessages.Should().HaveCount(1);
        DirectTestConsumer.ReceivedMessages.First().Value.Should().Be("mapping-test");

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task should_include_custom_headers_in_message()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging(x => x.AddProvider(LoggerProvider));
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "test-group";
            messaging.Version = "v1";
            messaging.WithTopicMapping<DirectTestMessage>("header-test-topic");
            messaging.Consumer<DirectTestConsumerWithHeaders>().Topic("header-test-topic").Build();
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var directPublisher = scope.ServiceProvider.GetRequiredService<IDirectPublisher>();

        var customHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["custom-header"] = "custom-value",
            [Headers.CorrelationId] = "correlation-123",
        };

        // when
        await directPublisher.PublishAsync(new DirectTestMessage("header-test"), customHeaders, AbortToken);

        var received = await DirectTestConsumerWithHeaders.WaitForContextAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        received.Should().BeTrue("Message should be delivered with custom headers");
        DirectTestConsumerWithHeaders.ReceivedContexts.Should().HaveCount(1);

        var ctx = DirectTestConsumerWithHeaders.ReceivedContexts.First();
        ctx.Headers["custom-header"].Should().Be("custom-value");
        ctx.Headers[Headers.CorrelationId].Should().Be("correlation-123");

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task should_send_multiple_messages_in_sequence()
    {
        // given
        const int messageCount = 5;

        var services = new ServiceCollection();
        services.AddLogging(x => x.AddProvider(LoggerProvider));
        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "test-group";
            messaging.Version = "v1";
            messaging.WithTopicMapping<DirectTestMessage>("sequential-test");
            messaging.Consumer<DirectTestConsumer>().Topic("sequential-test").Build();
            messaging.UseInMemoryMessageQueue();
            messaging.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var directPublisher = scope.ServiceProvider.GetRequiredService<IDirectPublisher>();

        // when - send multiple messages
        for (var i = 0; i < messageCount; i++)
        {
            await directPublisher.PublishAsync(new DirectTestMessage($"message-{i}"), AbortToken);
        }

        // Wait for all messages
        var received = await DirectTestConsumer.WaitForCountAsync(messageCount, TimeSpan.FromSeconds(10), AbortToken);

        // then
        received.Should().BeTrue("All messages should be received");
        DirectTestConsumer.ReceivedMessages.Should().HaveCount(messageCount);
        for (var i = 0; i < messageCount; i++)
        {
            DirectTestConsumer.ReceivedMessages.Should().Contain(m => m.Value == $"message-{i}");
        }

        await provider.DisposeAsync();
    }
}

public sealed record DirectTestMessage(string Value);

/// <summary>
/// Test consumer that collects received messages using static state for assertions.
/// Static state is required because DI creates its own instances of consumers.
/// </summary>
public sealed class DirectTestConsumer : IConsume<DirectTestMessage>
{
    private static readonly ConcurrentQueue<DirectTestMessage> _receivedMessages = new();
    private static readonly Lock _lock = new();
    private static TaskCompletionSource<bool> _messageReceivedTcs = new();
    private static int _expectedCount = 1;
    private static int _currentCount;

    public static IReadOnlyCollection<DirectTestMessage> ReceivedMessages => [.. _receivedMessages];

    public static void Reset()
    {
        while (_receivedMessages.TryDequeue(out _)) { }

        _messageReceivedTcs = new TaskCompletionSource<bool>();
        _expectedCount = 1;
        _currentCount = 0;
    }

    public ValueTask Consume(ConsumeContext<DirectTestMessage> context, CancellationToken cancellationToken)
    {
        _receivedMessages.Enqueue(context.Message);
        var count = Interlocked.Increment(ref _currentCount);
        if (count >= _expectedCount)
        {
            _messageReceivedTcs.TrySetResult(true);
        }

        return ValueTask.CompletedTask;
    }

    public static async Task<bool> WaitForMessageAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _messageReceivedTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<bool> WaitForCountAsync(
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        lock (_lock)
        {
            _expectedCount = count;
            if (_currentCount >= count)
            {
                return true;
            }

            _messageReceivedTcs = new TaskCompletionSource<bool>();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _messageReceivedTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// Test consumer that captures headers using static state for assertions.
/// Static state is required because DI creates its own instances of consumers.
/// </summary>
public sealed class DirectTestConsumerWithHeaders : IConsume<DirectTestMessage>
{
    private static readonly ConcurrentQueue<ConsumeContext<DirectTestMessage>> _receivedContexts = new();
    private static TaskCompletionSource<bool> _contextReceivedTcs = new();

    public static IReadOnlyCollection<ConsumeContext<DirectTestMessage>> ReceivedContexts => [.. _receivedContexts];

    public static void Reset()
    {
        while (_receivedContexts.TryDequeue(out _)) { }

        _contextReceivedTcs = new TaskCompletionSource<bool>();
    }

    public ValueTask Consume(ConsumeContext<DirectTestMessage> context, CancellationToken cancellationToken)
    {
        _receivedContexts.Enqueue(context);
        _contextReceivedTcs.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    public static async Task<bool> WaitForContextAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _contextReceivedTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
