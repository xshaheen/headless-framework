// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Headless.Fencing;
using Headless.Messaging;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Helpers;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests.Processor;

public sealed class ConsumerKeySequencersTests : TestBase
{
    private const string _Identity = "orders.projection";

    // A quarter of the grace is the pass interval; a claim is renewed every three passes.
    private static readonly TimeSpan _Tick = TimeSpan.FromSeconds(10);

    private static readonly RetryPolicyOptions _Policy = new()
    {
        InitialDispatchGrace = TimeSpan.FromSeconds(40),
        DispatchTimeout = TimeSpan.FromSeconds(90),
        LeaseRenewalInterval = TimeSpan.FromSeconds(30),
    };

    private readonly TimerArmedSignalClock _clock = new(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentDictionary<SequencedWork, Gate> _gates = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentQueue<string> _started = new();
    private readonly ConcurrentQueue<(string Call, Guid StorageId)> _holdCalls = new();
    private readonly Channel<bool> _holdCalled = Channel.CreateUnbounded<bool>();
    private Func<MediumMessage, ValueTask<bool>> _defer = _ => ValueTask.FromResult(true);
    private readonly Func<MediumMessage, ValueTask<bool>> _renew = _ => ValueTask.FromResult(true);

    [Fact]
    public async Task should_run_the_messages_of_one_key_one_at_a_time_in_hand_over_order()
    {
        // given
        var sut = _CreateSut();
        var first = _Work("a-1", key: "a", concurrency: 4);
        var second = _Work("a-2", key: "a", concurrency: 4);
        var third = _Work("a-3", key: "a", concurrency: 4);

        // when
        foreach (var work in new[] { first, second, third })
        {
            (await sut.EnqueueAsync(work, AbortToken)).Should().BeTrue();
        }

        // then
        await _Gate(first).Started.Task.Bounded();
        _Gate(second).Started.Task.IsCompleted.Should().BeFalse("a key runs one message at a time");
        _Gate(first).Release.TrySetResult();
        await _Gate(second).Started.Task.Bounded();
        _Gate(third).Started.Task.IsCompleted.Should().BeFalse();
        _Gate(second).Release.TrySetResult();
        await _Gate(third).Started.Task.Bounded();
        _Gate(third).Release.TrySetResult();
        await _FinishAsync(sut, first, second, third);
        _started.Should().Equal("a-1", "a-2", "a-3");
    }

    [Fact]
    public async Task should_run_different_keys_side_by_side_up_to_the_concurrency()
    {
        // given
        var sut = _CreateSut();
        var a = _Work("a-1", key: "a", concurrency: 2);
        var b = _Work("b-1", key: "b", concurrency: 2);
        var c = _Work("c-1", key: "c", concurrency: 2);

        // when
        await sut.EnqueueAsync(a, AbortToken);
        await sut.EnqueueAsync(b, AbortToken);
        await sut.EnqueueAsync(c, AbortToken);

        // then
        await Task.WhenAll(_Gate(a).Started.Task, _Gate(b).Started.Task).Bounded();
        _Gate(c).Started.Task.IsCompleted.Should().BeFalse("both concurrency slots are taken");
        _Gate(a).Release.TrySetResult();
        await _Gate(c).Started.Task.Bounded();
        await _FinishAsync(sut, a, b, c);
    }

    [Fact]
    public async Task should_not_order_unkeyed_messages_against_each_other()
    {
        // given
        var sut = _CreateSut();
        var first = _Work("u-1", key: null, concurrency: 2);
        var second = _Work("u-2", key: null, concurrency: 2);

        // when
        await sut.EnqueueAsync(first, AbortToken);
        await sut.EnqueueAsync(second, AbortToken);

        // then
        await Task.WhenAll(_Gate(first).Started.Task, _Gate(second).Started.Task).Bounded();
        await _FinishAsync(sut, first, second);
    }

    [Fact]
    public async Task should_hold_an_unkeyed_message_to_the_concurrency()
    {
        // given
        var sut = _CreateSut();
        var keyed = _Work("a-1", key: "a", concurrency: 1);
        var unkeyed = _Work("u-1", key: null, concurrency: 1);

        // when
        await sut.EnqueueAsync(keyed, AbortToken);
        await sut.EnqueueAsync(unkeyed, AbortToken);

        // then
        await _Gate(keyed).Started.Task.Bounded();
        _Gate(unkeyed).Started.Task.IsCompleted.Should().BeFalse("the one slot is taken");
        _Gate(keyed).Release.TrySetResult();
        await _Gate(unkeyed).Started.Task.Bounded();
        await _FinishAsync(sut, keyed, unkeyed);
    }

    [Fact]
    public async Task should_give_a_waiting_key_its_turn_before_a_busy_key_continues()
    {
        // given
        var sut = _CreateSut();
        var works = new[]
        {
            _Work("a-1", key: "a", concurrency: 1),
            _Work("a-2", key: "a", concurrency: 1),
            _Work("a-3", key: "a", concurrency: 1),
            _Work("b-1", key: "b", concurrency: 1),
        };

        foreach (var work in works)
        {
            await sut.EnqueueAsync(work, AbortToken);
        }

        // when
        await _Gate(works[0]).Started.Task.Bounded();
        foreach (var work in works)
        {
            _Gate(work).Release.TrySetResult();
        }

        await Task.WhenAll(works.Select(work => _Gate(work).Finished.Task)).Bounded();

        // then
        await _FinishAsync(sut, works);
        _started.Should().Equal("a-1", "b-1", "a-2", "a-3");
    }

    [Fact]
    public async Task should_order_two_identities_apart()
    {
        // given
        var sut = _CreateSut();
        var orders = _Work("orders-1", key: "42", concurrency: 1);
        var billing = _Work("billing-1", key: "42", concurrency: 1, identity: "billing.projection");

        // when
        await sut.EnqueueAsync(orders, AbortToken);
        await sut.EnqueueAsync(billing, AbortToken);

        // then
        await Task.WhenAll(_Gate(orders).Started.Task, _Gate(billing).Started.Task).Bounded();
        await _FinishAsync(sut, orders, billing);
    }

    [Fact]
    public async Task should_make_a_hand_over_wait_while_the_buffer_is_full()
    {
        // given: one slot, so the buffer holds 32 messages, the running one included
        var sut = _CreateSut();
        var buffered = Enumerable.Range(0, 32).Select(i => _Work($"a-{i}", key: "a", concurrency: 1)).ToArray();
        foreach (var work in buffered)
        {
            await sut.EnqueueAsync(work, AbortToken);
        }

        await _Gate(buffered[0]).Started.Task.Bounded();
        var overflow = _Work("a-overflow", key: "a", concurrency: 1);

        // when
        var handOver = sut.EnqueueAsync(overflow, AbortToken).AsTask();

        // then
        handOver.IsCompleted.Should().BeFalse("the buffer is full");
        _Gate(buffered[0]).Release.TrySetResult();
        (await handOver.Bounded()).Should().BeTrue();

        foreach (var work in buffered.Skip(1).Append(overflow))
        {
            _Gate(work).Release.TrySetResult();
        }

        await _FinishAsync(sut, [.. buffered, overflow]);
    }

    [Fact]
    public async Task should_cancel_a_hand_over_that_waits_for_room()
    {
        // given
        var sut = _CreateSut();
        var buffered = Enumerable.Range(0, 32).Select(i => _Work($"a-{i}", key: "a", concurrency: 1)).ToArray();
        foreach (var work in buffered)
        {
            await sut.EnqueueAsync(work, AbortToken);
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when: the hand-over waits for room, then its token is cancelled
        var act = async () =>
        {
            var handOver = sut.EnqueueAsync(_Work("late", key: "a", concurrency: 1), stopping.Token);
            handOver.IsCompleted.Should().BeFalse("the buffer is full");
            await stopping.CancelAsync();
            await handOver.AsTask().Bounded();
        };

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _FinishAsync(sut, buffered);
    }

    [Fact]
    public async Task should_keep_running_a_key_after_one_of_its_messages_faults_outside_the_handler()
    {
        // given
        var faulted = _Work("a-1", key: "a", concurrency: 1);
        var next = _Work("a-2", key: "a", concurrency: 1);
        var sut = _CreateSut(work =>
            ReferenceEquals(work, faulted) ? throw new InvalidOperationException("dispatch fault") : _RunAsync(work)
        );

        // when
        await sut.EnqueueAsync(faulted, AbortToken);
        await sut.EnqueueAsync(next, AbortToken);

        // then
        await _Gate(next).Started.Task.Bounded();
        await _FinishAsync(sut, next);
    }

    [Fact]
    public async Task should_return_unstarted_messages_and_refuse_new_ones_once_stopped()
    {
        // given
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var queuedBehindKey = _Work("a-2", key: "a", concurrency: 1);
        var queuedForSlot = _Work("b-1", key: "b", concurrency: 1);
        var unkeyed = _Work("u-1", key: null, concurrency: 1);
        foreach (var work in new[] { running, queuedBehindKey, queuedForSlot, unkeyed })
        {
            await sut.EnqueueAsync(work, AbortToken);
        }

        await _Gate(running).Started.Task.Bounded();

        // when
        var unstarted = await sut.StopAsync().Bounded();

        // then
        unstarted.Should().BeEquivalentTo([queuedBehindKey, queuedForSlot, unkeyed]);
        (await sut.EnqueueAsync(_Work("late", key: "a", concurrency: 1), AbortToken)).Should().BeFalse();
        sut.Completion.IsCompleted.Should().BeFalse("a message that started is still running");
        _Gate(running).Release.TrySetResult();
        await sut.Completion.Bounded();
        _started.Should().Equal("a-1");
    }

    [Fact]
    public async Task should_complete_at_once_when_stopped_idle()
    {
        // given
        var sut = _CreateSut();

        // when
        var unstarted = await sut.StopAsync().Bounded();

        // then
        unstarted.Should().BeEmpty();
        sut.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_put_off_the_pickup_of_a_waiting_row_on_the_second_pass_that_finds_it_waiting()
    {
        // given
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var waiting = _Work("a-2", key: "a", concurrency: 1);
        await sut.EnqueueAsync(running, AbortToken);
        await sut.EnqueueAsync(waiting, AbortToken);
        await _Gate(running).Started.Task.Bounded();

        // when
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 1);

        // then
        _holdCalls.Should().Equal(("defer", waiting.Message.StorageId));
        await _FinishAsync(sut, running, waiting);
    }

    [Fact]
    public async Task should_put_off_the_pickup_again_every_second_pass()
    {
        // given
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var waiting = _Work("a-2", key: "a", concurrency: 1);
        await sut.EnqueueAsync(running, AbortToken);
        await sut.EnqueueAsync(waiting, AbortToken);
        await _Gate(running).Started.Task.Bounded();
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 1);

        // when
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 1);
        await _LastPassEndedAsync();

        // then
        _holdCalls.Should().Equal(("defer", waiting.Message.StorageId), ("defer", waiting.Message.StorageId));
        await _FinishAsync(sut, running, waiting);
    }

    [Fact]
    public async Task should_renew_the_claim_of_a_waiting_retry_at_half_the_cadence()
    {
        // given
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var retry = _Work("a-2", key: "a", concurrency: 1);
        retry.Message.Lease = _ActiveLease();
        await sut.EnqueueAsync(running, AbortToken);
        await sut.EnqueueAsync(retry, AbortToken);
        await _Gate(running).Started.Task.Bounded();

        // when: the first renewal comes after one pass, the next after the full three
        await _PassAsync(expectedCalls: 1);
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 1);
        await _LastPassEndedAsync();

        // then
        _holdCalls.Should().Equal(("renew", retry.Message.StorageId), ("renew", retry.Message.StorageId));
        await _FinishAsync(sut, running, retry);
    }

    [Fact]
    public async Task should_not_hold_a_row_that_started_before_the_pass()
    {
        // given
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var next = _Work("a-2", key: "a", concurrency: 1);
        var waiting = _Work("a-3", key: "a", concurrency: 1);
        foreach (var work in new[] { running, next, waiting })
        {
            await sut.EnqueueAsync(work, AbortToken);
        }

        await _Gate(running).Started.Task.Bounded();
        await _PassAsync(expectedCalls: 0);
        _Gate(running).Release.TrySetResult();
        await _Gate(next).Started.Task.Bounded();

        // when
        await _PassAsync(expectedCalls: 1);

        // then
        _holdCalls.Should().Equal(("defer", waiting.Message.StorageId));
        await _FinishAsync(sut, running, next, waiting);
    }

    [Fact]
    public async Task should_try_again_on_the_next_pass_when_a_store_call_throws()
    {
        // given
        var attempts = 0;
        _defer = _ =>
            Interlocked.Increment(ref attempts) == 1
                ? ValueTask.FromException<bool>(new InvalidOperationException("store blip"))
                : ValueTask.FromResult(true);
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var waiting = _Work("a-2", key: "a", concurrency: 1);
        await sut.EnqueueAsync(running, AbortToken);
        await sut.EnqueueAsync(waiting, AbortToken);
        await _Gate(running).Started.Task.Bounded();
        await _PassAsync(expectedCalls: 0);

        // when
        await _PassAsync(expectedCalls: 1);
        await _PassAsync(expectedCalls: 1);

        // then
        _holdCalls.Should().HaveCount(2).And.OnlyContain(call => call.Call == "defer");
        await _FinishAsync(sut, running, waiting);
    }

    [Fact]
    public async Task should_stop_putting_off_pickups_when_the_store_cannot()
    {
        // given
        _defer = _ => ValueTask.FromException<bool>(new NotSupportedException());
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var waiting = _Work("a-2", key: "a", concurrency: 1);
        var alsoWaiting = _Work("a-3", key: "a", concurrency: 1);
        foreach (var work in new[] { running, waiting, alsoWaiting })
        {
            await sut.EnqueueAsync(work, AbortToken);
        }

        await _Gate(running).Started.Task.Bounded();
        await _PassAsync(expectedCalls: 0);

        // when: the first refused call ends the deferrals, so the second row is never tried
        await _PassAsync(expectedCalls: 1);
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 0);
        await _LastPassEndedAsync();

        // then
        _holdCalls.Should().ContainSingle();
        await _FinishAsync(sut, running, waiting, alsoWaiting);
    }

    [Fact]
    public async Task should_stop_holding_a_row_another_attempt_took()
    {
        // given
        _defer = _ => ValueTask.FromResult(false);
        var sut = _CreateSut();
        var running = _Work("a-1", key: "a", concurrency: 1);
        var waiting = _Work("a-2", key: "a", concurrency: 1);
        await sut.EnqueueAsync(running, AbortToken);
        await sut.EnqueueAsync(waiting, AbortToken);
        await _Gate(running).Started.Task.Bounded();
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 1);

        // when
        await _PassAsync(expectedCalls: 0);
        await _PassAsync(expectedCalls: 0);
        await _LastPassEndedAsync();

        // then
        _holdCalls.Should().ContainSingle();
        await _FinishAsync(sut, running, waiting);
    }

    private ConsumerKeySequencers _CreateSut(Func<SequencedWork, Task>? run = null)
    {
        return new ConsumerKeySequencers(
            run ?? _RunAsync,
            (message, _, _) => _Record("defer", message, _defer),
            (message, _, _) => _Record("renew", message, _renew),
            _Policy,
            _clock,
            TimeProvider.System,
            NullLogger.Instance
        );
    }

    private ValueTask<bool> _Record(string call, MediumMessage message, Func<MediumMessage, ValueTask<bool>> inner)
    {
        _holdCalls.Enqueue((call, message.StorageId));
        _holdCalled.Writer.TryWrite(true);
        return inner(message);
    }

    private async Task _RunAsync(SequencedWork work)
    {
        var gate = _Gate(work);
        _started.Enqueue(work.Message.Origin.Headers[MessagingHeaders.MessageId]!);
        gate.Started.SetResult();
        await gate.Release.Task;
        gate.Finished.SetResult();
    }

    private Gate _Gate(SequencedWork work) => _gates.GetOrAdd(work, static _ => new Gate());

    // One keeper pass: advance once the loop has armed its delay, which also shows the pass before it ended, then
    // wait for this pass's store calls.
    private async Task _PassAsync(int expectedCalls)
    {
        await _clock.AdvanceWhenArmedAsync(_Tick);
        for (var call = 0; call < expectedCalls; call++)
        {
            await _holdCalled.Reader.ReadAsync(AbortToken).Bounded();
        }
    }

    // The last pass ended once the loop armed its next delay. Nothing may advance the clock after this.
    private Task _LastPassEndedAsync() => _clock.WaitForArmedTimerAsync();

    // Lets every message run to its end, then stops: a stop must find nothing left to return.
    private async Task _FinishAsync(ConsumerKeySequencers sut, params SequencedWork[] works)
    {
        foreach (var work in works)
        {
            _Gate(work).Release.TrySetResult();
        }

        await Task.WhenAll(works.Select(work => _Gate(work).Finished.Task)).Bounded();
        (await sut.StopAsync().Bounded()).Should().BeEmpty();
        await sut.Completion.Bounded();
    }

    private static SequencedWork _Work(string id, string? key, byte concurrency, string identity = _Identity)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [MessagingHeaders.MessageId] = id };
        if (key is not null)
        {
            headers[MessagingHeaders.RoutingAffinityKey] = key;
        }

        return new SequencedWork(
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = new Message(headers, null),
                Content = "{}",
                Lane = MessageLane.Bus,
            },
            new ConsumerExecutorDescriptor
            {
                ConsumerType = typeof(ConsumerKeySequencersTests),
                MessageName = "orders.placed",
                SubscriptionName = identity,
                ConsumerIdentity = identity,
                Lane = MessageLane.Bus,
                Concurrency = concurrency,
                Sequential = true,
            },
            RetryAttempt: null
        );
    }

    private static RowLeaseSnapshot _ActiveLease() =>
        new(1, RowLeaseState.Active, DateTimeOffset.UtcNow.AddSeconds(90), "node-a", 0);

    private sealed class Gate
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
