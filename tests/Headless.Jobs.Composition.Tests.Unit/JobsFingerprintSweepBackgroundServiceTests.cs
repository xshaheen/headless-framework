// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The periodic sweep's pacing: a pass held back by fleet disagreement retries soon, because the fleet can converge at
/// any moment, while an ordinary pass waits the configured interval.
/// </summary>
public sealed class JobsFingerprintSweepBackgroundServiceTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromHours(1);

    [Fact]
    public async Task should_retry_within_five_minutes_after_a_deferred_pass_and_wait_the_interval_after_an_agreed_one()
    {
        var time = new DelayRecordingTimeProvider();
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                _Result(CronFingerprintSweepDeferralReason.FleetDisagreement),
                _Result(CronFingerprintSweepDeferralReason.None)
            );
        using var service = new JobsFingerprintSweepBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FingerprintSweepInterval = _Interval },
            time,
            NullLogger<JobsFingerprintSweepBackgroundService>.Instance
        );

        await service.StartAsync(AbortToken);

        (await time.NextDelayAsync(AbortToken)).Should().Be(_Interval, "the first pass waits out the interval");
        time.Advance(_Interval);
        (await time.NextDelayAsync(AbortToken))
            .Should()
            .Be(TimeSpan.FromMinutes(5), "a deferred pass retries soon so rebases resume once the fleet agrees");
        time.Advance(TimeSpan.FromMinutes(5));
        (await time.NextDelayAsync(AbortToken)).Should().Be(_Interval, "an agreed pass returns to the interval");

        await service.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_restart_from_a_fresh_snapshot_after_a_deferred_pass()
    {
        var time = new DelayRecordingTimeProvider();
        var cursor = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var snapshot = Guid.Parse("00000000-0000-0000-0000-000000000009");
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                // The pass reaches its per-pass bound with rows left, so the service would normally resume here.
                _Result(CronFingerprintSweepDeferralReason.None) with
                {
                    HasMore = true,
                    NextCursorId = cursor,
                    SnapshotHighWatermarkId = snapshot,
                },
                _Result(CronFingerprintSweepDeferralReason.FleetDisagreement),
                _Result(CronFingerprintSweepDeferralReason.None)
            );
        using var service = new JobsFingerprintSweepBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FingerprintSweepInterval = _Interval, FingerprintSweepBatchSize = 1 },
            time,
            NullLogger<JobsFingerprintSweepBackgroundService>.Instance
        );

        await service.StartAsync(AbortToken);
        await time.NextDelayAsync(AbortToken);
        time.Advance(_Interval);
        await time.NextDelayAsync(AbortToken);
        time.Advance(TimeSpan.FromMinutes(5));
        await time.NextDelayAsync(AbortToken);
        await service.StopAsync(AbortToken);

        // First pass: page one, then the deferred page that resumed from its cursor. Second pass: a fresh snapshot.
        Received.InOrder(() =>
        {
            _ = manager.RebaseStaleFingerprintsAsync(1, null, null, true, Arg.Any<CancellationToken>());
            _ = manager.RebaseStaleFingerprintsAsync(1, cursor, snapshot, true, Arg.Any<CancellationToken>());
            _ = manager.RebaseStaleFingerprintsAsync(1, null, null, true, Arg.Any<CancellationToken>());
        });
    }

    private static CronFingerprintSweepResult _Result(CronFingerprintSweepDeferralReason deferral) =>
        new()
        {
            Scanned = 0,
            Rebased = 0,
            Deferred = 0,
            LostFence = 0,
            HasMore = false,
            DeferralReason = deferral,
        };

    /// <summary>Hands the test every delay the service schedules, in order, so pacing is asserted without racing.</summary>
    private sealed class DelayRecordingTimeProvider : FakeTimeProvider
    {
        private readonly Channel<TimeSpan> _delays = Channel.CreateUnbounded<TimeSpan>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _delays.Writer.TryWrite(dueTime);

            return base.CreateTimer(callback, state, dueTime, period);
        }

        public async Task<TimeSpan> NextDelayAsync(CancellationToken cancellationToken) =>
            await _delays.Reader.ReadAsync(cancellationToken);
    }
}
