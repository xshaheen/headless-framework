// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class ConsumerClientPauseResumeTests
{
    [Fact]
    public async Task pause_async_default_implementation_should_complete_without_throwing()
    {
        // given
        IConsumerClient client = new MinimalConsumerClient();

        // when
        var act = async () => await client.PauseAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task resume_async_default_implementation_should_complete_without_throwing()
    {
        // given
        IConsumerClient client = new MinimalConsumerClient();

        // when
        var act = async () => await client.ResumeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task pause_async_is_idempotent_when_called_multiple_times()
    {
        // given
        IConsumerClient client = new MinimalConsumerClient();

        // when
        var act = async () =>
        {
            await client.PauseAsync();
            await client.PauseAsync();
            await client.PauseAsync();
        };

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task resume_async_is_idempotent_when_called_multiple_times()
    {
        // given
        IConsumerClient client = new MinimalConsumerClient();

        // when
        var act = async () =>
        {
            await client.ResumeAsync();
            await client.ResumeAsync();
            await client.ResumeAsync();
        };

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task pause_async_respects_cancellation_token_parameter()
    {
        // given
        IConsumerClient client = new MinimalConsumerClient();
        using var cts = new CancellationTokenSource();

        // when
        var act = async () => await client.PauseAsync(cts.Token);

        // then — default impl ignores the token, should still complete
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task resume_async_respects_cancellation_token_parameter()
    {
        // given
        IConsumerClient client = new MinimalConsumerClient();
        using var cts = new CancellationTokenSource();

        // when
        var act = async () => await client.ResumeAsync(cts.Token);

        // then — default impl ignores the token, should still complete
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Minimal implementation of <see cref="IConsumerClient"/> that does not override
    /// <see cref="IConsumerClient.PauseAsync"/> or <see cref="IConsumerClient.ResumeAsync"/>,
    /// exercising the default interface method implementations.
    /// </summary>
    private sealed class MinimalConsumerClient : IConsumerClient
    {
        public BrokerAddress BrokerAddress => default;

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public ValueTask SubscribeAsync(IEnumerable<string> topics) => ValueTask.CompletedTask;

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask CommitAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask RejectAsync(object? sender) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
