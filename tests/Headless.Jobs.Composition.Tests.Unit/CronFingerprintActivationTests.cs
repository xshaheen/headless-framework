// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CronFingerprintActivationTests : TestBase
{
    [Fact]
    public async Task should_drain_one_forward_only_snapshot_before_activation_completes()
    {
        var firstCursor = new Guid("00000002-0000-0000-0000-000000000000");
        var highWatermark = new Guid("00000003-0000-0000-0000-000000000000");
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(2, null, null, false, AbortToken)
            .Returns(
                new CronFingerprintSweepResult
                {
                    Scanned = 2,
                    Rebased = 1,
                    Deferred = 1,
                    LostFence = 0,
                    HasMore = true,
                    NextCursorId = firstCursor,
                    SnapshotHighWatermarkId = highWatermark,
                }
            );
        manager
            .RebaseStaleFingerprintsAsync(2, firstCursor, highWatermark, false, AbortToken)
            .Returns(
                new CronFingerprintSweepResult
                {
                    Scanned = 1,
                    Rebased = 1,
                    Deferred = 0,
                    LostFence = 0,
                    HasMore = false,
                    NextCursorId = highWatermark,
                    SnapshotHighWatermarkId = highWatermark,
                }
            );
        var barrier = new JobsActivationBarrier();
        var warmup = _Create(manager, barrier, new SchedulerOptionsBuilder { FingerprintSweepBatchSize = 2 });

        await warmup.WarmUpAsync(AbortToken);

        barrier.IsActivated.Should().BeTrue();
        await manager.Received(1).RebaseStaleFingerprintsAsync(2, null, null, false, AbortToken);
        await manager.Received(1).RebaseStaleFingerprintsAsync(2, firstCursor, highWatermark, false, AbortToken);
        await manager
            .Received(2)
            .RebaseStaleFingerprintsAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), false, AbortToken);
    }

    [Fact]
    public async Task should_keep_the_barrier_closed_when_fingerprint_storage_fails()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(100, null, null, false, AbortToken)
            .Returns<Task<CronFingerprintSweepResult>>(_ => throw new InvalidOperationException("store unavailable"));
        var barrier = new JobsActivationBarrier();
        var warmup = _Create(manager, barrier, new SchedulerOptionsBuilder());

        var act = () => warmup.WarmUpAsync(AbortToken);

        // The failure propagates so the warm-up runner retries it, and it is not published to the waiting loops:
        // they keep waiting for a drain that succeeds instead of stopping for the host's lifetime.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("store unavailable");
        barrier.IsActivated.Should().BeFalse();
        barrier.WaitAsync(AbortToken).IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_the_barrier_closed_when_a_page_reports_more_rows_without_a_cursor()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(100, null, null, false, AbortToken)
            .Returns(
                new CronFingerprintSweepResult
                {
                    Scanned = 1,
                    Rebased = 0,
                    Deferred = 0,
                    LostFence = 0,
                    HasMore = true,
                    NextCursorId = null,
                }
            );
        var barrier = new JobsActivationBarrier();
        var warmup = _Create(manager, barrier, new SchedulerOptionsBuilder());

        var act = () => warmup.WarmUpAsync(AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Fingerprint sweep reported more rows without a continuation cursor.");
        barrier.IsActivated.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_drain_again_once_activation_completed()
    {
        var manager = Substitute.For<IInternalJobManager>();
        var barrier = new JobsActivationBarrier();
        barrier.MarkCompleted();
        var warmup = _Create(manager, barrier, new SchedulerOptionsBuilder());

        await warmup.WarmUpAsync(AbortToken);

        await manager
            .DidNotReceive()
            .RebaseStaleFingerprintsAsync(
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void should_not_gate_the_warmup_readiness_check()
    {
        // The jobs readiness check already reports the barrier, so the drain does not hold the warmup check as well.
        var warmup = _Create(
            Substitute.For<IInternalJobManager>(),
            new JobsActivationBarrier(),
            new SchedulerOptionsBuilder()
        );

        ((IWarmup)warmup).GatesReadiness.Should().BeFalse();
    }

    private static JobsActivationWarmup _Create(
        IInternalJobManager manager,
        JobsActivationBarrier barrier,
        SchedulerOptionsBuilder schedulerOptions
    )
    {
        return new JobsActivationWarmup(manager, schedulerOptions, barrier, NullLogger<JobsActivationWarmup>.Instance);
    }
}
