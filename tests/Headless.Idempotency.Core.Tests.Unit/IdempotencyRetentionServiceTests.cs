// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Fencing;
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
    private readonly IFencedLeases _leases = Substitute.For<IFencedLeases>();
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task should_purge_record_batches_until_short_then_the_idempotency_leases()
    {
        // given
        var log = new ConcurrentQueue<string>();
        var leasesPurged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new Queue<int>([2, 1]);
        _store
            .PurgeAsync(TimeSpan.Zero, 2, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                log.Enqueue("records");
                return ValueTask.FromResult(batches.TryDequeue(out var deleted) ? deleted : 0);
            });
        _leases
            .PurgeAsync(IdempotentAdmission.LeaseKind, TimeSpan.FromDays(3), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                log.Enqueue("leases");
                leasesPurged.TrySetResult();
                return ValueTask.FromResult(0);
            });
        using var service = _Service();

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(leasesPurged.Task);
        await service.StopAsync(AbortToken);

        // then — records go first, so no lease is purged while a record that names it survives
        log.Take(3).Should().Equal("records", "records", "leases");
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
        await _AdvanceUntilAsync(retried.Task);
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
        _time.Advance(TimeSpan.FromDays(1));
        await Task.Delay(50, AbortToken);

        // then — a disabled purge keeps re-checking on a fixed cadence instead of exiting the hosted service
        _store.ReceivedCalls().Should().BeEmpty();
        _leases.ReceivedCalls().Should().BeEmpty();
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
        _time.Advance(TimeSpan.FromDays(1));
        await Task.Delay(50, AbortToken);
        _store.ReceivedCalls().Should().BeEmpty("the interval is still null");

        // and then the option reloads back to a value on the already-running service
        _options.PurgeInterval = _Interval;
        await _AdvanceUntilAsync(purged.Task);
        await service.StopAsync(AbortToken);

        // then
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    private IdempotencyRetentionService _Service()
    {
        var monitor = Substitute.For<IOptionsMonitor<IdempotentOperationsOptions>>();
        monitor.CurrentValue.Returns(_ => _options);

        return new IdempotencyRetentionService(
            _store,
            _leases,
            monitor,
            _time,
            NullLogger<IdempotencyRetentionService>.Instance
        );
    }

    // The service's loop runs on its own thread, so its next delay may not be registered at the moment the test
    // advances the clock; advancing repeatedly until the expected effect lands avoids racing it.
    private async Task _AdvanceUntilAsync(Task effect)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!effect.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The retention service did not reach the expected state.");
            }

            _time.Advance(_Interval);
            await Task.Delay(10, AbortToken);
        }

        await effect;
    }
}
