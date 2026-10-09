// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

public sealed class ConsumerClientDeadLetterTests : TestBase
{
    [Fact]
    public async Task should_commit_the_message_when_the_transport_does_not_dead_letter()
    {
        // given — a transport written before dead-lettering existed
        await using var commitOnly = new CommitOnlyConsumerClient();
        IConsumerClient client = commitOnly;
        var sender = new object();

        // when
        await client.DeadLetterAsync(sender, "SubscriberNotFound", "no consumer", AbortToken);

        // then
        commitOnly.CommittedSenders.Should().ContainSingle().Which.Should().BeSameAs(sender);
    }

    private sealed class CommitOnlyConsumerClient : IConsumerClient
    {
        public List<object?> CommittedSenders { get; } = [];

        public BrokerAddress BrokerAddress => default;

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

        public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
        {
            CommittedSenders.Add(sender);
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
