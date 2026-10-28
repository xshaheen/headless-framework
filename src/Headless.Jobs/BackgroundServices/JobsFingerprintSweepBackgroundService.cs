// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Rebases cron definitions whose schedule-interpretation rules changed underneath them — a tzdata update that moves
/// a zone's transitions, or a cron-library upgrade that reads a field differently — while the expression and timezone
/// string stay byte-identical.
/// </summary>
/// <remarks>
/// <b>Its own service rather than a branch in the scheduler loop.</b> It selects on the exact OPPOSITE
/// criterion from dispatch: a rule change that moves an occurrence <i>earlier</i> is hidden behind the stale later
/// projection, so a sweep keyed on due-ness would systematically skip the definitions that most need rebasing. Two
/// opposed selection criteria in one loop is how one of them ends up quietly subordinated to the other.
/// <para>Rules change on the timescale of an OS package update, so this runs on a long period rather than the scheduler cadence.</para>
/// </remarks>
internal sealed class JobsFingerprintSweepBackgroundService(
    IInternalJobManager internalJobsManager,
    SchedulerOptionsBuilder schedulerOptions,
    TimeProvider timeProvider,
    ILogger<JobsFingerprintSweepBackgroundService> logger
) : BackgroundService
{
    private readonly TimeSpan _period = schedulerOptions.FingerprintSweepInterval;
    private readonly int _batchSize = schedulerOptions.FingerprintSweepBatchSize;
    private Guid? _cursor;
    private Guid? _highWatermark;
    private TimeSpan _nextDelay = schedulerOptions.FingerprintSweepInterval;
    private const int _MaxConsecutiveFullBatches = 100;

    // A deferred pass is waiting for the fleet to converge, which a restart or a membership read can settle at any
    // moment; the full interval (an hour by default) would leave every stale definition stale that long after it does.
    private static readonly TimeSpan _DeferredRetryCeiling = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initialization drains one complete store snapshot before the scheduler is allowed to start. Periodic sweeps
        // therefore wait out the configured interval and only handle later rule/environment drift.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timeProvider.Delay(_nextDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var scanned = 0;
                var rebased = 0;
                var deferred = 0;
                var lostFence = 0;
                var batches = 0;
                var wrapped = false;
                CronFingerprintSweepResult result;

                do
                {
                    result = await internalJobsManager
                        .RebaseStaleFingerprintsAsync(
                            _batchSize,
                            afterId: _cursor,
                            throughId: _highWatermark,
                            allowWrap: !wrapped,
                            cancellationToken: stoppingToken
                        )
                        .ConfigureAwait(false);
                    batches++;
                    scanned += result.Scanned;
                    rebased += result.Rebased;
                    deferred += result.Deferred;
                    lostFence += result.LostFence;

                    if (result.HasMore && result.NextCursorId is null)
                    {
                        throw new InvalidOperationException(
                            "Fingerprint sweep reported more rows without a continuation cursor."
                        );
                    }

                    if (result.DeferralReason != CronFingerprintSweepDeferralReason.None)
                    {
                        break;
                    }

                    _cursor = result.NextCursorId;
                    _highWatermark ??= result.SnapshotHighWatermarkId;
                    wrapped |= result.Wrapped;
                } while (result.HasMore && !wrapped && batches < _MaxConsecutiveFullBatches);

                if (result.DeferralReason != CronFingerprintSweepDeferralReason.None)
                {
                    // Rebases resume from a fresh snapshot once the fleet agrees: the retained one was read under
                    // a verdict that no longer holds.
                    _cursor = null;
                    _highWatermark = null;
                    _nextDelay = _period < _DeferredRetryCeiling ? _period : _DeferredRetryCeiling;
                    JobsFingerprintSweepLog.SweepCompleted(
                        logger,
                        scanned,
                        rebased,
                        deferred,
                        lostFence,
                        result.DeferralReason
                    );

                    continue;
                }

                _nextDelay = _period;

                if (result.HasMore)
                {
                    JobsFingerprintSweepLog.DrainBoundReached(logger, batches * _batchSize);
                }

                if (!result.HasMore || wrapped)
                {
                    _cursor = null;
                    _highWatermark = null;
                }

                JobsFingerprintSweepLog.SweepCompleted(
                    logger,
                    scanned,
                    rebased,
                    deferred,
                    lostFence,
                    CronFingerprintSweepDeferralReason.None
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                JobsFingerprintSweepLog.SweepFailed(logger, exception);
            }
        }
    }
}

internal static partial class JobsFingerprintSweepLog
{
    [LoggerMessage(
        EventId = 3230,
        Level = LogLevel.Information,
        Message = "Cron fingerprint sweep completed: scanned={Scanned}, rebased={Rebased}, deferred={Deferred}, "
            + "lostFence={LostFence}, passDeferredBy={Deferral}."
    )]
    internal static partial void SweepCompleted(
        ILogger logger,
        int scanned,
        int rebased,
        int deferred,
        int lostFence,
        CronFingerprintSweepDeferralReason deferral
    );

    [LoggerMessage(
        EventId = 3231,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint sweep failed; the saved cursor will retry on the next interval."
    )]
    internal static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3232,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint sweep reached its per-pass bound after scanning {Count} definitions; continuation "
            + "state is retained for the next interval."
    )]
    internal static partial void DrainBoundReached(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3233,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint rebases have been deferred for over {HoldoutAfter} because nodes that run the "
            + "fingerprint sweep publish different cron rules. This node publishes {LocalDigest}; disagreeing nodes by "
            + "digest: {DigestGroups}. Give every sweeping node the same OS family and tzdata, or disable background "
            + "services on the outliers."
    )]
    internal static partial void FleetHoldout(
        ILogger logger,
        TimeSpan holdoutAfter,
        string localDigest,
        string digestGroups
    );

    [LoggerMessage(
        EventId = 3234,
        Level = LogLevel.Warning,
        Message = "Cron fingerprint rebases deferred: the coordination membership snapshot could not be read, so fleet "
            + "agreement on cron rules cannot be shown. The next sweep retries within five minutes."
    )]
    internal static partial void MembershipUnavailable(ILogger logger, Exception exception);
}
