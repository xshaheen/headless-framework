// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class ConsumerClientPauseResumeTests
{
    [Fact]
    public void pause_async_is_required_interface_member()
    {
        // PauseAsync has no default implementation — any IConsumerClient must provide it.
        // MinimalConsumerClient compiles only because it explicitly implements PauseAsync.
        var method = typeof(IConsumerClient).GetMethod(
            nameof(IConsumerClient.PauseAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(CancellationToken)],
            modifiers: null
        );
        method.Should().NotBeNull();
        method!.IsAbstract.Should().BeTrue("PauseAsync must not have a default implementation");
    }

    [Fact]
    public void resume_async_is_required_interface_member()
    {
        var method = typeof(IConsumerClient).GetMethod(
            nameof(IConsumerClient.ResumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(CancellationToken)],
            modifiers: null
        );
        method.Should().NotBeNull();
        method!.IsAbstract.Should().BeTrue("ResumeAsync must not have a default implementation");
    }

    [Fact]
    public async Task should_complete_without_throwing_when_pause_async()
    {
        // given
        await using IConsumerClient client = new MinimalConsumerClient();

        // when
        var act = async () => await client.PauseAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_complete_without_throwing_when_resume_async()
    {
        // given
        await using IConsumerClient client = new MinimalConsumerClient();

        // when
        var act = async () => await client.ResumeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task pause_async_is_idempotent_when_called_multiple_times()
    {
        // given
        await using IConsumerClient client = new MinimalConsumerClient();

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
        await using IConsumerClient client = new MinimalConsumerClient();

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
    public async Task pause_async_accepts_cancellation_token()
    {
        // given
        await using IConsumerClient client = new MinimalConsumerClient();
        using var cts = new CancellationTokenSource();

        // when
        var act = async () => await client.PauseAsync(cts.Token);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task resume_async_accepts_cancellation_token()
    {
        // given
        await using IConsumerClient client = new MinimalConsumerClient();
        using var cts = new CancellationTokenSource();

        // when
        var act = async () => await client.ResumeAsync(cts.Token);

        // then
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Minimal implementation of <see cref="IConsumerClient"/> that provides no-op
    /// <see cref="IConsumerClient.PauseAsync"/> and <see cref="IConsumerClient.ResumeAsync"/>
    /// implementations, since these are now required interface members.
    /// </summary>
    private sealed class MinimalConsumerClient : IConsumerClient
    {
        public BrokerAddress BrokerAddress => default;

        public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

        public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

        public ValueTask SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
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
