// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class InMemoryQueueTransportTests : TestBase
{
    private readonly InMemoryQueueTransport _transport;
    private readonly InMemoryConsumerClient _consumerClient;

    public InMemoryQueueTransportTests()
    {
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryQueueTransport>>();

        var queue = new MemoryQueue(queueLogger);
        _transport = new InMemoryQueueTransport(queue, transportLogger);
        _consumerClient = new InMemoryConsumerClient(queue, "test-group", 1, IntentType.Queue);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _consumerClient.DisposeAsync();
        await _transport.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public void should_return_in_memory_broker_address()
    {
        // when
        var address = _transport.BrokerAddress;

        // then
        address.Name.Should().Be("InMemory");
        address.Endpoint.Should().Be("localhost");
    }

    [Fact]
    public async Task should_send_message_successfully()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-messageName"]);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumerClient.OnMessageCallback = (msg, _) =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = _StartListening(cts.Token);

        var message = _CreateTestMessage("msg-1", "test-messageName");

        // when
        var result = await _transport.SendAsync(message, AbortToken);

        var receivedMessage = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listenTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        receivedMessage.Id.Should().Be("msg-1");
    }

    [Fact]
    public async Task should_return_success_result_on_send()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-messageName"]);
        var message = _CreateTestMessage("msg-1", "test-messageName");

        // when
        var result = await _transport.SendAsync(message, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task should_return_success_when_no_subscriber()
    {
        // given — no subscriber registered; real-broker semantics: publish-without-subscriber is a no-op
        var message = _CreateTestMessage("msg-1", "unsubscribed-messageName");

        // when
        var result = await _transport.SendAsync(message, AbortToken);

        // then — message is silently dropped; transport reports success (the send itself did not fail)
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_support_message_headers()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-messageName"]);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumerClient.OnMessageCallback = (msg, _) =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = _StartListening(cts.Token);

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "test-messageName",
            [Headers.CorrelationId] = "corr-123",
            ["custom-header"] = "custom-value",
        };

        var message = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);

        // when
        await _transport.SendAsync(message, AbortToken);

        var receivedMessage = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listenTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        receivedMessage.GetCorrelationId().Should().Be("corr-123");
        receivedMessage.Headers["custom-header"].Should().Be("custom-value");
    }

    [Fact]
    public async Task should_send_message_body()
    {
        // given
        await _consumerClient.SubscribeAsync(["test-messageName"]);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumerClient.OnMessageCallback = (msg, _) =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listenTask = _StartListening(cts.Token);

        var bodyContent = "{\"key\":\"value\"}"u8.ToArray();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "msg-1",
            [Headers.MessageName] = "test-messageName",
        };
        var message = new TransportMessage(headers, bodyContent);

        // when
        await _transport.SendAsync(message, AbortToken);

        var receivedMessage = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cts.CancelAsync();
        await listenTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        receivedMessage.Body.ToArray().Should().Equal(bodyContent);
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryQueueTransport>>();
        var queue = new MemoryQueue(queueLogger);
        await using var transport = new InMemoryQueueTransport(queue, transportLogger);

        // when
        var action = async () => await transport.DisposeAsync();

        // then
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_be_thread_safe_for_concurrent_sends()
    {
        // given
        await _consumerClient.SubscribeAsync(["concurrent-messageName"]);

        var receivedMessages = new List<TransportMessage>();
        var lockObj = new Lock();
        const int messageCount = 50;
        var tcs = new TaskCompletionSource();
        var count = 0;

        _consumerClient.OnMessageCallback = async (msg, sender) =>
        {
            lock (lockObj)
            {
                receivedMessages.Add(msg);
                count++;
                if (count >= messageCount)
                {
                    tcs.TrySetResult();
                }
            }

            await _consumerClient.CommitAsync(sender);
        };

        using var cts = new CancellationTokenSource();
        var listenTask = Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(30), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        // when - send messages concurrently via transport
        var sendTasks = Enumerable
            .Range(0, messageCount)
            .Select(i => _transport.SendAsync(_CreateTestMessage($"msg-{i}", "concurrent-messageName"), AbortToken));

        var results = await Task.WhenAll(sendTasks);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        await cts.CancelAsync();
        await listenTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
        receivedMessages.Should().HaveCount(messageCount);
    }

    [Fact]
    public async Task should_deliver_to_exactly_one_competing_worker()
    {
        // given
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryQueueTransport>>();
        var queue = new MemoryQueue(queueLogger);
        await using var transport = new InMemoryQueueTransport(queue, transportLogger);
        await using var worker1 = new InMemoryConsumerClient(queue, "workers", 1, IntentType.Queue);
        await using var worker2 = new InMemoryConsumerClient(queue, "workers", 1, IntentType.Queue);

        await worker1.SubscribeAsync(["jobs"]);
        await worker2.SubscribeAsync(["jobs"]);

        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task onMessage(TransportMessage _, object? __)
        {
            if (Interlocked.Increment(ref received) == 1)
            {
                tcs.TrySetResult();
            }

            return Task.CompletedTask;
        }

        worker1.OnMessageCallback = onMessage;
        worker2.OnMessageCallback = onMessage;

        using var cts = new CancellationTokenSource();
        var listen1 = Task.Run(
            async () =>
            {
                try
                {
                    await worker1.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );
        var listen2 = Task.Run(
            async () =>
            {
                try
                {
                    await worker2.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        // when
        var result = await transport.SendAsync(_CreateTestMessage("job-1", "jobs"), AbortToken);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();
        await Task.WhenAll(listen1, listen2).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        received.Should().Be(1);
    }

    private static TransportMessage _CreateTestMessage(string id, string messageName)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = id,
            [Headers.MessageName] = messageName,
        };

        return new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);
    }

    private Task _StartListening(CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    await _consumerClient.ListeningAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );
    }
}
