// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transport;

public sealed class RedeliveryBackoffTests : TestBase
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(60, 30)]
    public void redelivery_delay_doubles_from_one_second_up_to_thirty(long failures, int nominalSeconds)
    {
        var delays = Enumerable
            .Range(0, 50)
            .Select(seed => RedeliveryBackoff.RedeliveryDelay(failures, new Random(seed)))
            .ToArray();

        // Jitter spreads each delay down to three quarters of its nominal value, never above it.
        var nominal = TimeSpan.FromSeconds(nominalSeconds);
        delays.Should().OnlyContain(delay => delay >= nominal * 0.75 && delay <= nominal);
        delays.Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void admission_retries_stay_far_inside_a_broker_lock_window()
    {
        var total = Enumerable
            .Range(1, RedeliveryBackoff.AdmissionRetries)
            .Select(retry => RedeliveryBackoff.AdmissionRetryDelay(retry, new Random(retry)))
            .Aggregate(TimeSpan.Zero, (sum, delay) => sum + delay);

        total.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(1.75));
    }

    [Fact]
    public async Task delay_then_reject_holds_the_delivery_for_the_delay()
    {
        // given
        var clock = new FakeTimeProvider();
        var client = Substitute.For<IConsumerClient>();

        // when
        var reject = RedeliveryBackoff
            .DelayThenRejectAsync(client, "token", TimeSpan.FromSeconds(5), clock, AbortToken)
            .AsTask();
        clock.Advance(TimeSpan.FromSeconds(4));

        // then
        reject.IsCompleted.Should().BeFalse();
        await client.DidNotReceive().RejectAsync(Arg.Any<object?>(), Arg.Any<CancellationToken>());

        clock.Advance(TimeSpan.FromSeconds(1));
        await reject;
        await client.Received(1).RejectAsync("token", CancellationToken.None);
    }

    [Fact]
    public async Task delay_then_reject_rejects_at_once_when_the_host_stops()
    {
        // given
        var clock = new FakeTimeProvider();
        var client = Substitute.For<IConsumerClient>();
        using var stopping = new CancellationTokenSource();

        // when: the host stops while the delivery waits
        var reject = RedeliveryBackoff
            .DelayThenRejectAsync(client, "token", TimeSpan.FromSeconds(30), clock, stopping.Token)
            .AsTask();
        await stopping.CancelAsync();
        await reject;

        // then: the reject still runs, uncancelled, because settlement is must-complete
        await client.Received(1).RejectAsync("token", CancellationToken.None);
    }

    [Fact]
    public async Task default_delayed_reject_falls_back_to_a_plain_reject()
    {
        // given: a transport that implements only the plain reject
        var client = new PlainRejectClient();

        // when: the host is already stopping, so the default does not wait
        await ((IConsumerClient)client).RejectAsync(
            "token",
            TimeSpan.FromSeconds(30),
            new CancellationToken(canceled: true)
        );

        // then
        client.Rejected.Should().ContainSingle().Which.Should().Be("token");
    }

    private sealed class PlainRejectClient : IConsumerClient
    {
        public List<object?> Rejected { get; } = [];

        public BrokerAddress BrokerAddress => new("test", "plain");

        public Func<TransportMessage, object?, Task>? OnMessageCallback => null;

        public Action<LogMessageEventArgs>? OnLogCallback => null;

        public void AttachCallbacks(
            Func<TransportMessage, object?, Task>? onMessage,
            Action<LogMessageEventArgs>? onLog
        ) { }

        public ValueTask<ICollection<string>> FetchMessageNamesAsync(
            IEnumerable<string> messageNames,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<ICollection<string>>(messageNames.ToArray());

        public ValueTask SubscribeAsync(
            IEnumerable<string> messageNames,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
        {
            Rejected.Add(sender);
            return ValueTask.CompletedTask;
        }

        public ValueTask PauseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ResumeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
