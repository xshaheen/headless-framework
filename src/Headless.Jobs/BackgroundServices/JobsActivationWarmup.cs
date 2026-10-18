// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Drains one stable cron fingerprint snapshot after the host has started, then opens
/// <see cref="JobsActivationBarrier" /> so the job loops parked on it may select work.
/// </summary>
/// <remarks>
/// <para>
/// The drain costs one store query per page plus one write per stale row, so it runs after the start instead of
/// holding it open. Nothing selects work early because of that: every loop that selects or claims cron work waits on
/// the barrier, and only a completed drain opens it.
/// </para>
/// <para>
/// It fails closed. A storage or provider failure leaves the barrier closed and propagates, so the warm-up runner
/// retries it with back-off; the loops keep waiting and the <c>jobs</c> readiness check stays <c>Unhealthy</c>
/// meanwhile. It does not gate the <c>warmup</c> check because the <c>jobs</c> check already reports the same state.
/// </para>
/// <para>
/// A repeated run after a successful one does nothing: the barrier opens once per host and an open barrier means the
/// snapshot was already drained.
/// </para>
/// </remarks>
internal sealed class JobsActivationWarmup(
    IInternalJobManager manager,
    SchedulerOptionsBuilder schedulerOptions,
    JobsActivationBarrier activationBarrier,
    ILogger<JobsActivationWarmup> logger
) : IWarmup
{
    public string Name => "JobsActivation";

    public bool GatesReadiness => false;

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        if (activationBarrier.IsActivated)
        {
            return;
        }

        await DrainFingerprintSnapshotAsync(cancellationToken).ConfigureAwait(false);
        activationBarrier.MarkCompleted();
    }

    internal async Task DrainFingerprintSnapshotAsync(CancellationToken cancellationToken)
    {
        Guid? cursor = null;
        Guid? highWatermark = null;
        var scanned = 0;
        var rebased = 0;
        var deferred = 0;
        var lostFence = 0;

        while (true)
        {
            var result = await manager
                .RebaseStaleFingerprintsAsync(
                    schedulerOptions.FingerprintSweepBatchSize,
                    afterId: cursor,
                    throughId: highWatermark,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            scanned += result.Scanned;
            rebased += result.Rebased;
            deferred += result.Deferred;
            lostFence += result.LostFence;
            cursor = result.NextCursorId;
            highWatermark ??= result.SnapshotHighWatermarkId;

            if (!result.HasMore)
            {
                break;
            }

            if (cursor is null)
            {
                throw new InvalidOperationException(
                    "Fingerprint sweep reported more rows without a continuation cursor."
                );
            }
        }

        logger.CronFingerprintActivationCompleted(scanned, rebased, deferred, lostFence);
    }
}
