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

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        EventId = 3231,
        Level = LogLevel.Warning,
        Message = "The cron evaluation-fingerprint sweep failed; affected definitions keep dispatching under their "
            + "previous interpretation until the next sweep succeeds."
    )]
    public static partial void SweepFailed(ILogger logger, Exception exception);
}
