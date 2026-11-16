// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class IdempotencyRetentionServiceTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromHours(1);

    private readonly IdempotentOperationsOptions _options = new()
    {
        PurgeInterval = _Interval,
        PurgeBatchSize = 2,
        DefaultRetention = TimeSpan.FromDays(3),
    };

    private readonly IIdempotencyRecordStore _store = Substitute.For<IIdempotencyRecordStore>();
    private readonly ArmedTimerTimeProvider _time = new();

    // Every loop round reads the options first, so a read proves ExecuteAsync is running. BackgroundService starts it
    // through Task.Run with the stopping token, and a stop that lands before the thread pool picks it up ends
    // ExecuteTask canceled without the loop ever running, so tests that assert a clean stop wait for this first.
    private readonly TaskCompletionSource _loopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task should_purge_record_batches_until_one_comes_back_short()
    {
        // given
        var log = new ConcurrentQueue<int>();
        var shortBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new Queue<int>([2, 1]);
        _store
            .PurgeAsync(TimeSpan.Zero, 2, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var deleted = batches.TryDequeue(out var next) ? next : 0;
                log.Enqueue(deleted);

                if (deleted < 2)
                {
                    shortBatch.TrySetResult();
                }

                return ValueTask.FromResult(deleted);
            });
        using var service = _Service();

        // when
        await service.StartAsync(AbortToken);
        await _time.WaitForTimerAsync();
        _time.Advance(_Interval);
        await shortBatch.Task.Bounded();
        await service.StopAsync(AbortToken);

        // then - a full batch is followed by another; the short one ends the run
        log.Take(2).Should().Equal(2, 1);
    }

    [Fact]
    public async Task should_log_a_failed_purge_and_try_again_next_interval()
    {
        // given
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _store
            .PurgeAsync(TimeSpan.Zero, 2, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return ValueTask.FromException<int>(new InvalidOperationException("database down"));
                }

                retried.TrySetResult();
                return ValueTask.FromResult(0);
            });
        using var service = _Service();

        // when
        await service.StartAsync(AbortToken);
        await _time.WaitForTimerAsync();
        _time.Advance(_Interval);
        await _time.WaitForTimerAsync();
        _time.Advance(_Interval);
        await retried.Task.Bounded();
        await service.StopAsync(AbortToken);

        // then
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_cleanly_while_waiting_for_the_next_interval()
    {
        // given
        using var service = _Service();
        await service.StartAsync(AbortToken);
        await _loopStarted.Task.Bounded();

        // when
        await service.StopAsync(AbortToken);

        // then
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_run_when_the_purge_is_disabled()
    {
        // given
        _options.PurgeInterval = null;
        using var service = _Service();

        // when
        await service.StartAsync(AbortToken);
        await _time.WaitForTimerAsync();
        _time.Advance(_Interval);

        // The next re-check delay is armed only after the loop read the options again.
        await _time.WaitForTimerAsync();

        // then — a disabled purge keeps re-checking on a fixed cadence instead of exiting the hosted service
        _store.ReceivedCalls().Should().BeEmpty();
        service.ExecuteTask!.IsCompleted.Should().BeFalse();

        await service.StopAsync(AbortToken);
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue("it still stops cleanly on shutdown");
    }

    [Fact]
    public async Task should_resume_purging_once_the_interval_reloads_from_null_to_a_value()
    {
        // given
        _options.PurgeInterval = null;
        var purged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store
            .PurgeAsync(TimeSpan.Zero, 2, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                purged.TrySetResult();
                return ValueTask.FromResult(0);
            });
        using var service = _Service();

        // when — still disabled: the re-check tick alone must not purge anything
        await service.StartAsync(AbortToken);
        await _time.WaitForTimerAsync();
        _time.Advance(_Interval);
        await _time.WaitForTimerAsync();
        _store.ReceivedCalls().Should().BeEmpty("the interval is still null");

        // and then the option reloads back to a value on the already-running service
        _options.PurgeInterval = _Interval;
        _time.Advance(_Interval);
        await _time.WaitForTimerAsync();
        _time.Advance(_Interval);
        await purged.Task.Bounded();
        await service.StopAsync(AbortToken);

        // then
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    private IdempotencyRetentionService _Service()
    {
        var monitor = Substitute.For<IOptionsMonitor<IdempotentOperationsOptions>>();
        monitor.CurrentValue.Returns(_ =>
        {
            _loopStarted.TrySetResult();
            return _options;
        });

        return new IdempotencyRetentionService(
            _store,
            monitor,
            _time,
            NullLogger<IdempotencyRetentionService>.Instance
        );
    }

    // The service's loop runs on its own thread, so its next delay may not exist when the test advances the clock, and
    // an advance made before the timer is armed is lost. The tests advance only after this reports the armed timer.
    private sealed class ArmedTimerTimeProvider : FakeTimeProvider
    {
        private readonly Channel<bool> _armed = Channel.CreateUnbounded<bool>();

        public Task WaitForTimerAsync() => _armed.Reader.ReadAsync(AbortToken).Bounded();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _armed.Writer.TryWrite(true);

            return timer;
        }
    }
}
