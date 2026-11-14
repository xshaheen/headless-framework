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

    [Fact]
    public async Task should_report_a_row_as_waiting_only_while_its_delivery_waits_for_a_permit()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer(
            "orders.audit",
            "orders.placed",
            ConsumerRateLimit.FixedWindow(1, TimeSpan.FromSeconds(1))
        );
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        // when
        await sut.WaitAsync(consumer, first, AbortToken);
        var waiting = sut.WaitAsync(consumer, second, AbortToken).AsTask();

        // then
        sut.IsWaiting(first).Should().BeFalse("a delivery that got its permit at once never waited");
        sut.IsWaiting(second).Should().BeTrue();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        sut.IsWaiting(second).Should().BeFalse();
    }

    [Fact]
    public async Task should_stop_reporting_a_row_as_waiting_when_its_wait_is_cancelled()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer("orders.audit", "orders.placed", ConsumerRateLimit.FixedWindow(1, _Hour));
        var row = Guid.NewGuid();
        await sut.WaitAsync(consumer, Guid.NewGuid(), AbortToken);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
#pragma warning disable AsyncFixer04 // False positive: the task is started to observe it while it waits, and is awaited before the using scope ends.
        var waiting = sut.WaitAsync(consumer, row, shutdown.Token).AsTask();
#pragma warning restore AsyncFixer04

        // when
        await shutdown.CancelAsync();
        var act = () => waiting.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        sut.IsWaiting(row).Should().BeFalse();
    }

    [Fact]
    public void should_take_a_permit_without_waiting_and_refuse_once_the_rate_is_spent()
    {
        // given
        using var sut = new ConsumerRateLimiters();
        var limited = _Consumer("orders.audit", "orders.placed", ConsumerRateLimit.FixedWindow(2, _Hour));
        var unlimited = _Consumer("orders.billing", "orders.placed", rateLimit: null);

        // when / then
        sut.TryAcquire(limited).Should().BeTrue();
        sut.TryAcquire(limited).Should().BeTrue();
        sut.TryAcquire(limited).Should().BeFalse();
        sut.TryAcquire(limited).Should().BeFalse("a queued acquisition is not granted before the rate renews");
        sut.TryAcquire(unlimited).Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_a_place_in_the_queue_for_a_refused_caller_so_waiting_deliveries_cannot_starve_it()
    {
        // given: the permit is spent, and a refused non-waiting call queues ahead of a later waiting delivery
        using var sut = new ConsumerRateLimiters();
        var consumer = _Consumer(
            "orders.audit",
            "orders.placed",
            ConsumerRateLimit.FixedWindow(1, TimeSpan.FromSeconds(1))
        );
        await sut.WaitAsync(consumer, AbortToken);
        sut.TryAcquire(consumer).Should().BeFalse();
        var laterDelivery = sut.WaitAsync(consumer, AbortToken).AsTask();

        // when: two windows renew, one permit each, oldest first
        await laterDelivery.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then: the first permit went to the queued acquisition, and the next call takes it
        sut.TryAcquire(consumer).Should().BeTrue();
    }

    [Fact]
    public void should_refuse_a_permit_once_disposed()
    {
        // given
        var sut = new ConsumerRateLimiters();
        var consumer = _Consumer("orders.audit", "orders.placed", ConsumerRateLimit.FixedWindow(1, _Hour));
        var late = _Consumer("orders.billing", "orders.placed", ConsumerRateLimit.FixedWindow(1, _Hour));
        sut.TryAcquire(consumer).Should().BeTrue();
        sut.TryAcquire(consumer).Should().BeFalse();

        // when
        sut.Dispose();

        // then
        sut.TryAcquire(consumer).Should().BeFalse();
        sut.TryAcquire(late).Should().BeFalse();
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
