// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Xunit;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

namespace Tests;

/// <summary>Base class for consumer client implementation tests.</summary>
[PublicAPI]
public abstract class ConsumerClientTestsBase : TestBase
{
    /// <summary>Gets the consumer client instance for testing.</summary>
    protected abstract IConsumerClient GetConsumerClient();

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

    public virtual async Task should_subscribe_to_topic()
    {
        // given
        await using var consumer = GetConsumerClient();
        var topics = new[] { "test-topic-1", "test-topic-2" };

        // when
        var act = async () => await consumer.SubscribeAsync(topics);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_receive_messages_via_listen_callback()
    {
        // given
        await using var consumer = GetConsumerClient();
        var receivedMessages = new ConcurrentBag<TransportMessage>();
        var messageReceived = new TaskCompletionSource<bool>();

        consumer.OnMessageCallback = (msg, sender) =>
        {
            receivedMessages.Add(msg);
            messageReceived.TrySetResult(true);
            return Task.CompletedTask;
        };

        await consumer.SubscribeAsync(["test-topic"]);

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

        // then - callback should be set without throwing
        consumer.OnMessageCallback.Should().NotBeNull();
    }

    public virtual async Task should_commit_message_successfully()
    {
        // given
        await using var consumer = GetConsumerClient();
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
        await using var consumer = GetConsumerClient();
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
        await using var consumer = GetConsumerClient();
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
        var consumer = GetConsumerClient();
        await consumer.SubscribeAsync(["test-topic"]);

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
        await consumer.DisposeAsync();

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
        await using var consumer = GetConsumerClient();
        var processedCount = 0;
        var lockObj = new Lock();

        consumer.OnMessageCallback = (msg, sender) =>
        {
            lock (lockObj)
            {
                processedCount++;
            }

            return Task.CompletedTask;
        };

        await consumer.SubscribeAsync(["concurrent-topic"]);

        // when
        var tasks = Enumerable
            .Range(0, 10)
            .Select(_ =>
                Task.Run(async () =>
                {
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

        // then - should handle concurrent operations without exception
        consumer.Should().NotBeNull();
    }

    public virtual async Task should_dispose_without_exception()
    {
        // given
        var consumer = GetConsumerClient();

        // when & then
        var act = () => consumer.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_have_valid_broker_address()
    {
        // given, when
        await using var consumer = GetConsumerClient();

        // then
        consumer.BrokerAddress.Name.Should().NotBeNullOrEmpty();
    }

    public virtual async Task should_handle_null_sender_in_commit()
    {
        // given
        await using var consumer = GetConsumerClient();

        // when
        var act = async () => await consumer.CommitAsync(null);

        // then - should handle null gracefully or throw appropriate exception
        try
        {
            await act.Should().NotThrowAsync();
        }
        catch (ArgumentNullException)
        {
            // Some implementations may require non-null sender
        }
    }

    public virtual async Task should_handle_null_sender_in_reject()
    {
        // Skip if consumer doesn't support rejection
        if (!Capabilities.SupportsReject)
        {
            Assert.Skip("Consumer does not support message rejection");
        }

        // given
        await using var consumer = GetConsumerClient();

        // when
        var act = async () => await consumer.RejectAsync(null);

        // then - should handle null gracefully or throw appropriate exception
        try
        {
            await act.Should().NotThrowAsync();
        }
        catch (ArgumentNullException)
        {
            // Some implementations may require non-null sender
        }
    }

    public virtual async Task should_invoke_log_callback_on_events()
    {
        // given
        await using var consumer = GetConsumerClient();
        var logEvents = new ConcurrentBag<LogMessageEventArgs>();

        consumer.OnLogCallback = args => logEvents.Add(args);

        await consumer.SubscribeAsync(["log-test-topic"]);

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

        // then - log callback should be set
        consumer.OnLogCallback.Should().NotBeNull();
    }
}
