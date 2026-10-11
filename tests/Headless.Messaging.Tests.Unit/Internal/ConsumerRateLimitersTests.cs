// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class ConsumerRateLimitersTests : TestBase
{
    // A window this long never ends during a test, so a delivery over the limit can only wait or be cancelled.
    private static readonly TimeSpan _Hour = TimeSpan.FromHours(1);

    [Fact]
    public async Task should_return_at_once_for_a_consumer_without_a_rate_limit()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer("orders.audit", "orders.placed", rateLimit: null);

        // when
        var waits = Enumerable.Range(0, 100).Select(_ => sut.WaitAsync(consumer, AbortToken).AsTask()).ToArray();

        // then
        waits.Should().OnlyContain(wait => wait.IsCompletedSuccessfully);
        await Task.WhenAll(waits);
    }

    [Fact]
    public async Task should_hold_a_burst_beyond_the_permit_limit_until_the_window_renews()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer(
            "orders.audit",
            "orders.placed",
            ConsumerRateLimit.FixedWindow(2, TimeSpan.FromSeconds(1))
        );

        // when
        var first = sut.WaitAsync(consumer, AbortToken).AsTask();
        var second = sut.WaitAsync(consumer, AbortToken).AsTask();
        var third = sut.WaitAsync(consumer, AbortToken).AsTask();

        // then
        first.IsCompletedSuccessfully.Should().BeTrue();
        second.IsCompletedSuccessfully.Should().BeTrue();
        third.IsCompleted.Should().BeFalse();
        await Task.WhenAll(first, second);
        await third.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    [Fact]
    public async Task should_release_a_waiting_delivery_promptly_when_its_token_is_cancelled()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer("orders.audit", "orders.placed", ConsumerRateLimit.FixedWindow(1, _Hour));
        await sut.WaitAsync(consumer, AbortToken);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
#pragma warning disable AsyncFixer04 // False positive: the task is started to observe it while it waits, and is awaited before the using scope ends.
        var waiting = sut.WaitAsync(consumer, shutdown.Token).AsTask();
#pragma warning restore AsyncFixer04

        // when
        await shutdown.CancelAsync();

        // then
        var act = () => waiting.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_share_one_limiter_across_the_messages_and_lanes_of_one_identity()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var rateLimit = ConsumerRateLimit.FixedWindow(1, _Hour);
        var placed = _Consumer("orders.audit", "orders.placed", rateLimit);
        var cancelled = _Consumer("orders.audit", "orders.cancelled", rateLimit, MessageLane.Queue);
        await sut.WaitAsync(placed, AbortToken);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
#pragma warning disable AsyncFixer04 // False positive: the task is started to observe it while it waits, and is awaited before the using scope ends.
        var waiting = sut.WaitAsync(cancelled, shutdown.Token).AsTask();
#pragma warning restore AsyncFixer04

        // then
        waiting.IsCompleted.Should().BeFalse();
        await shutdown.CancelAsync();
        var act = () => waiting.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_give_each_identity_its_own_limiter()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var rateLimit = ConsumerRateLimit.FixedWindow(1, _Hour);
        await sut.WaitAsync(_Consumer("orders.audit", "orders.placed", rateLimit), AbortToken);

        // when
        var other = sut.WaitAsync(_Consumer("orders.billing", "orders.placed", rateLimit), AbortToken).AsTask();

        // then
        other.IsCompletedSuccessfully.Should().BeTrue();
        await other;
    }

    [Fact]
    public async Task should_report_a_disposed_limiter_as_a_cancellation()
    {
        // given
        var sut = new ConsumerRateLimiters();
        var consumer = _Consumer("orders.audit", "orders.placed", ConsumerRateLimit.FixedWindow(1, _Hour));
        await sut.WaitAsync(consumer, AbortToken);
        var waiting = sut.WaitAsync(consumer, AbortToken).AsTask();

        // when
        sut.Dispose();

        // then
        var queued = () => waiting.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await queued.Should().ThrowAsync<OperationCanceledException>();
        var later = async () => await sut.WaitAsync(consumer, AbortToken);
        await later.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_save_up_a_token_bucket_burst_and_then_wait_for_replenishment()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer("orders.audit", "orders.placed", ConsumerRateLimit.TokenBucket(3, 1, _Hour));
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var burst = Enumerable.Range(0, 3).Select(_ => sut.WaitAsync(consumer, AbortToken).AsTask()).ToArray();
#pragma warning disable AsyncFixer04 // False positive: the task is started to observe it while it waits, and is awaited before the using scope ends.
        var waiting = sut.WaitAsync(consumer, shutdown.Token).AsTask();
#pragma warning restore AsyncFixer04

        // then
        burst.Should().OnlyContain(wait => wait.IsCompletedSuccessfully);
        waiting.IsCompleted.Should().BeFalse();
        await Task.WhenAll(burst);
        await shutdown.CancelAsync();
        var act = () => waiting.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ConsumerExecutorDescriptor _Consumer(
        string identity,
        string messageName,
        ConsumerRateLimit? rateLimit,
        MessageLane lane = MessageLane.Bus
    ) =>
        new()
        {
            ConsumerType = typeof(ConsumerRateLimitersTests),
            MessageName = messageName,
            SubscriptionName = identity,
            ConsumerIdentity = identity,
            Lane = lane,
            RateLimit = rateLimit,
        };
}
