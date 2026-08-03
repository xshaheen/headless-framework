// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs.BackgroundServices;

/// <summary>
/// Rebases cron definitions whose schedule-interpretation rules changed underneath them — a tzdata update that moves
/// a zone's transitions, or a cron-library upgrade that reads a field differently — while the expression and timezone
/// string stay byte-identical.
/// </summary>
/// <remarks>
/// <b>Its own service rather than a branch in the scheduler loop (KTD7).</b> It selects on the exact OPPOSITE
/// criterion from dispatch: a rule change that moves an occurrence <i>earlier</i> is hidden behind the stale later
/// projection, so a sweep keyed on due-ness would systematically skip the definitions that most need rebasing. Two
/// opposed selection criteria in one loop is how one of them ends up quietly subordinated to the other.
/// <para>
/// Rules change on the timescale of an OS package update, so this runs on a long period rather than the scheduler
/// cadence. It is also deliberately best-effort: a sweep that throws must not take the host down, because a stale
/// projection still dispatches — just under the old interpretation — whereas a dead host dispatches nothing.
/// </para>
/// </remarks>
internal sealed class JobsFingerprintSweepBackgroundService(
    IInternalJobManager internalJobsManager,
    SchedulerOptionsBuilder schedulerOptions,
    TimeProvider timeProvider,
    ILogger<JobsFingerprintSweepBackgroundService> logger
) : BackgroundService
{
    private int _started;
    private readonly TimeSpan _period = schedulerOptions.FingerprintSweepInterval;
    private readonly int _batchSize = schedulerOptions.FingerprintSweepBatchSize;

    // Back-to-back full batches before the sweep returns to its interval. At the default batch size this drains 10,000
    // definitions in one pass, which is far beyond any realistic cron-definition count, so the bound is a backstop
    // against a provider that never persists rather than a limit real deployments meet.
    private const int _MaxConsecutiveFullBatches = 100;

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFullBatches = 0;

        // Sweep once at startup before waiting out a period: a process that just came up on a host with new tzdata is
        // the single most likely moment for a fingerprint to be stale, and making it wait out the interval means
        // dispatching under the old interpretation for exactly as long as the interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var rebased = await internalJobsManager
                    .RebaseStaleFingerprintsAsync(_batchSize, stoppingToken)
                    .ConfigureAwait(false);

                if (rebased > 0)
                {
                    JobsFingerprintSweepLog.RebasedDefinitions(logger, rebased);
                }

                if (rebased >= _batchSize && ++consecutiveFullBatches < _MaxConsecutiveFullBatches)
                {
                    // A full batch means the store almost certainly holds more. The event this sweep exists for — a
                    // tzdata update — stales every definition in the affected zone AT ONCE, so waiting out the
                    // interval between batches would drain a large set at batch-size-per-interval and keep dispatching
                    // under the old interpretation for hours. Loop straight into the next batch instead; the sweep
                    // settles back onto the interval as soon as one batch comes back short.
                    continue;
                }

                if (consecutiveFullBatches >= _MaxConsecutiveFullBatches)
                {
                    // Progress is guaranteed only while a rebase actually persists a current fingerprint, which is a
                    // provider contract this service cannot verify. A provider that reports success without persisting
                    // would otherwise keep this loop hammering the store with no delay between batches, so the drain is
                    // bounded and what it stopped short of is reported rather than passed over in silence.
                    JobsFingerprintSweepLog.DrainBoundReached(logger, _MaxConsecutiveFullBatches * _batchSize);
                }

                consecutiveFullBatches = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable ERP022 // Best-effort by design: see the class remarks.
            catch (Exception exception)
            {
                JobsFingerprintSweepLog.SweepFailed(logger, exception);
            }
#pragma warning restore ERP022

            try
            {
                await timeProvider.Delay(_period, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

internal static partial class JobsFingerprintSweepLog
{
    [LoggerMessage(
        EventId = 3230,
        Level = LogLevel.Information,
        Message = "Rebased {Count} cron definition(s) whose schedule-interpretation rules had changed."
    )]
    public static partial void RebasedDefinitions(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3232,
        Level = LogLevel.Warning,
        Message = "The cron evaluation-fingerprint sweep stopped after rebasing {Count} definition(s) in one pass and "
            + "will resume on its next interval; every batch was full, which can also mean rebases are not being "
            + "persisted."
    )]
    public static partial void DrainBoundReached(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3231,
        Level = LogLevel.Warning,
        Message = "The cron evaluation-fingerprint sweep failed; affected definitions keep dispatching under their "
            + "previous interpretation until the next sweep succeeds."
    )]
    public static partial void SweepFailed(ILogger logger, Exception exception);
}
