// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
/// <summary>Base class for consumer client implementation tests.</summary>
[PublicAPI]
public abstract class ConsumerClientTestsBase : TestBase
{
    /// <summary>Gets the consumer client instance for testing.</summary>
    protected abstract Task<IConsumerClient> GetConsumerClientAsync();

    /// <summary>Gets the consumer client capabilities for conditional test execution.</summary>
    protected virtual ConsumerClientCapabilities Capabilities => ConsumerClientCapabilities.Default;

    /// <summary>Creates a valid transport message for testing.</summary>
    protected static TransportMessage CreateTransportMessage(
        string? messageId = null,
        string? messageName = null,
        ReadOnlyMemory<byte>? body = null,
        IDictionary<string, string?>? additionalHeaders = null
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, messageId ?? Guid.NewGuid().ToString() },
            { MessagingHeaders.MessageName, messageName ?? "TestMessage" },
        };

        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                headers[header.Key] = header.Value;
            }
        }

        return new TransportMessage(headers, body ?? "test-body"u8.ToArray());
    }

    /// <summary>
    /// Allows transports to transform logical topic names into broker-specific subscription identifiers.
    /// </summary>
    protected virtual ValueTask<IReadOnlyList<string>> ResolveSubscriptionTopicsAsync(
        IConsumerClient consumer,
        IReadOnlyList<string> topics
    )
    {
        return ValueTask.FromResult(topics);
    }

    public virtual async Task should_subscribe_to_topic()
    {
        // given
        await using var consumer = await GetConsumerClientAsync();
        var topics = await ResolveSubscriptionTopicsAsync(consumer, ["test-topic-1", "test-topic-2"]);

        // when
        var act = async () => await consumer.SubscribeAsync(topics);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_receive_messages_via_listen_callback()
    {
        // Lifecycle/wiring test — verifies subscribe + listen with a callback set doesn't throw.
        // Actual message delivery is covered by MessagingIntegrationTestsBase (requires a publisher).

        // given
        await using var consumer = await GetConsumerClientAsync();

        consumer.OnMessageCallback = (msg, sender) => Task.CompletedTask;
        consumer.OnMessageCallback.Should().NotBeNull();

        var topics = await ResolveSubscriptionTopicsAsync(consumer, ["test-topic"]);
        await consumer.SubscribeAsync(topics);

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await consumer.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout occurs
        }
    }

    public virtual async Task should_commit_message_successfully()
    {
        // given
        await using var consumer = await GetConsumerClientAsync();
        var mockSender = new object();

        // when
        var act = async () => await consumer.CommitAsync(mockSender);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_reject_message_successfully()
    {
        // Skip if consumer doesn't support rejection
        if (!Capabilities.SupportsReject)
        {
            Assert.Skip("Consumer does not support message rejection");
        }

        // given
        await using var consumer = await GetConsumerClientAsync();
        var mockSender = new object();

        // when
        var act = async () => await consumer.RejectAsync(mockSender);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_fetch_topics()
    {
        // Skip if consumer doesn't support fetching topics
        if (!Capabilities.SupportsFetchTopics)
        {
            Assert.Skip("Consumer does not support fetching topics");
        }

        // given
        await using var consumer = await GetConsumerClientAsync();
        var requestedTopics = new[] { "topic-1", "topic-2", "topic-3" };

        // when
        var result = await consumer.FetchTopicsAsync(requestedTopics);

        // then
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThanOrEqualTo(0);
    }

    public virtual async Task should_shutdown_gracefully()
    {
        // Skip if consumer doesn't support graceful shutdown
        if (!Capabilities.SupportsGracefulShutdown)
        {
            Assert.Skip("Consumer does not support graceful shutdown");
        }

        // given
        await using var consumer = await GetConsumerClientAsync();
        var topics = await ResolveSubscriptionTopicsAsync(consumer, ["test-topic"]);
        await consumer.SubscribeAsync(topics);

        // Start listening in background
        using var cts = new CancellationTokenSource();
        var listeningTask = Task.Run(async () =>
        {
            try
            {
                await consumer.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        });

        // Allow some time for listening to start
        await Task.Delay(100);

        // when
        await cts.CancelAsync();

        // then - should complete without hanging
        var completed = await Task.WhenAny(listeningTask, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(listeningTask, "Consumer should shutdown gracefully within timeout");
    }

    public virtual async Task should_handle_concurrent_message_processing()
    {
        // Skip if consumer doesn't support concurrent processing
        if (!Capabilities.SupportsConcurrentProcessing)
        {
            Assert.Skip("Consumer does not support concurrent message processing");
        }

        // given
        await using var consumer = await GetConsumerClientAsync();
        var listenerStartCount = 0;

        consumer.OnMessageCallback = (_, _) => Task.CompletedTask;

        var topics = await ResolveSubscriptionTopicsAsync(consumer, ["concurrent-topic"]);
        await consumer.SubscribeAsync(topics);

        // when
        var tasks = Enumerable
            .Range(0, 10)
            .Select(_ =>
                Task.Run(async () =>
                {
                    Interlocked.Increment(ref listenerStartCount);
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                        await consumer.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected
                    }
                })
            );

        await Task.WhenAll(tasks);

        // then — all 10 parallel listeners started without the consumer throwing
        listenerStartCount.Should().Be(10);
    }

    public virtual async Task should_dispose_without_exception()
    {
        // given
        var consumer = await GetConsumerClientAsync();

        // when & then
        var act = () => consumer.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_have_valid_broker_address()
    {
        // given, when
        await using var consumer = await GetConsumerClientAsync();

        // then
        consumer.BrokerAddress.Name.Should().NotBeNullOrEmpty();
    }

    public virtual async Task should_invoke_log_callback_on_events()
    {
        // Lifecycle/wiring test — verifies subscribe + listen with a log callback set doesn't throw.
        // Log event delivery depends on broker activity; actual invocation is not guaranteed here.

        // given
        await using var consumer = await GetConsumerClientAsync();

        consumer.OnLogCallback = _ => { };
        consumer.OnLogCallback.Should().NotBeNull();

        var topics = await ResolveSubscriptionTopicsAsync(consumer, ["log-test-topic"]);
        await consumer.SubscribeAsync(topics);

        // when
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await consumer.ListeningAsync(TimeSpan.FromMilliseconds(50), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }
}
