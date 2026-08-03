// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Managers;

internal sealed class InternalJobsManager<TTimeJob, TCronJob>(
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    TimeProvider timeProvider,
    IJobsNotificationHubSender notificationHubSender,
    CronScheduleCache cronScheduleCache,
    ILogger<InternalJobsManager<TTimeJob, TCronJob>> logger,
    JobsRequestSerializationOptions serializationOptions,
    IGuidGenerator guidGenerator,
    IServiceProvider serviceProvider,
    SchedulerOptionsBuilder schedulerOptions
) : IInternalJobManager
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // R1/KTD1: start-tick of the last stranded-child sweep. long.MinValue means "never run", so the first poll after
    // startup always sweeps and a host that starts with an already-stranded child does not wait out an interval.
    private long _lastStrandedSweepTicks = long.MinValue;

    // Resolved from the container rather than taken as a constructor parameter: the recovery and rebase paths are the
    // only consumers, and threading a new required dependency through every construction site (including tests that
    // legitimately do not care about telemetry) would be churn for one optional signal. Null in hosts that register no
    // instrumentation, which is a supported configuration.
    private IJobsInstrumentation? _instrumentation;

    private IJobsInstrumentation? _ResolveInstrumentation()
    {
        return _instrumentation ??= serviceProvider.GetService<IJobsInstrumentation>();
    }

    private readonly TimeSpan _strandedSweepInterval = schedulerOptions.FallbackIntervalChecker;

    // R1/KTD1: claim the sweep slot at most once per FallbackIntervalChecker. GetNextJobs is the scheduler's hot path
    // — JobsSchedulerBackgroundService sleeps 1ms whenever work is due — and on the relational providers this backstop
    // runs a candidate scan, so sweeping per poll cost an unbounded scan at up to ~1kHz per node in EVERY deployment,
    // including ones that never enqueue a chain. The stamp is taken BEFORE the sweep (start-to-start spacing), so a
    // sweep that throws also waits out an interval instead of retrying against a struggling database every tick.
    private bool _TryEnterStrandedSweep()
    {
        var nowTicks = timeProvider.GetUtcNow().UtcDateTime.Ticks;
        var last = Volatile.Read(ref _lastStrandedSweepTicks);

        // The long.MinValue arm must be tested first: nowTicks - long.MinValue overflows.
        if (last != long.MinValue && nowTicks - last < _strandedSweepInterval.Ticks)
        {
            return false;
        }

        // Only one caller wins the slot; a concurrent poller skips rather than double-scanning.
        return Interlocked.CompareExchange(ref _lastStrandedSweepTicks, nowTicks, last) == last;
    }

    public async Task<(TimeSpan TimeRemaining, JobExecutionState[] Functions)> GetNextJobs(
        CancellationToken cancellationToken = default
    )
    {
        // U5/KTD3 safety net: skip (never release) idle timed children whose parent terminalized through a path that
        // missed the per-parent / set-based reconcile, so a missed terminalization can never permanently strand a
        // timed child. The skip side never makes a child eligible early, so running it before the peek is safe — it
        // can only remove candidates that must never run. Best-effort: a failure here must NOT block normal
        // scheduling; the fallback loop's set-based reconcile guarantees liveness regardless. Rate-limited to the
        // fallback cadence (R1/KTD1) — this is a missed-terminalization backstop, so eventual is sufficient.
        try
        {
            if (_TryEnterStrandedSweep())
            {
                await persistenceProvider.SkipStrandedTimedChildrenAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable ERP022 // The backstop is intentionally non-fatal to the scheduling poll (logged, not rethrown).
        catch (Exception exception)
        {
            logger.LogTimedChildSafetyNetFailed(exception);
        }
#pragma warning restore ERP022

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var minCronGroupTask = _GetEarliestCronJobGroupAsync(cancellationToken);
        var minTimeJobsTask = persistenceProvider.GetEarliestTimeJobsAsync(cancellationToken);

        await Task.WhenAll(minCronGroupTask, minTimeJobsTask).ConfigureAwait(false);

        var minCronGroup = await minCronGroupTask.ConfigureAwait(false);
        var minTimeJobs = await minTimeJobsTask.ConfigureAwait(false);

        var cronTime = minCronGroup?.Key;
        var timeJobTime = minTimeJobs.Length > 0 ? minTimeJobs[0].ExecutionTime : null;

        if (cronTime is null && timeJobTime is null)
        {
            return (Timeout.InfiniteTimeSpan, []);
        }

        TimeSpan timeRemaining;
        var includeCron = false;
        var includeTimeJobs = false;

        if (cronTime is null)
        {
            includeTimeJobs = true;
            timeRemaining = _SafeRemaining(timeJobTime!.Value, now);
        }
        else if (timeJobTime is null)
        {
            includeCron = true;
            timeRemaining = _SafeRemaining(cronTime.Value, now);
        }
        else
        {
            var cronSecond = new DateTime(
                cronTime.Value.Year,
                cronTime.Value.Month,
                cronTime.Value.Day,
                cronTime.Value.Hour,
                cronTime.Value.Minute,
                cronTime.Value.Second
            );
            var timeSecond = new DateTime(
                timeJobTime.Value.Year,
                timeJobTime.Value.Month,
                timeJobTime.Value.Day,
                timeJobTime.Value.Hour,
                timeJobTime.Value.Minute,
                timeJobTime.Value.Second
            );

            if (cronSecond == timeSecond)
            {
                includeCron = true;
                includeTimeJobs = true;
                var earliest = cronTime < timeJobTime ? cronTime.Value : timeJobTime.Value;
                timeRemaining = _SafeRemaining(earliest, now);
            }
            else if (cronTime < timeJobTime)
            {
                includeCron = true;
                timeRemaining = _SafeRemaining(cronTime.Value, now);
            }
            else
            {
                includeTimeJobs = true;
                timeRemaining = _SafeRemaining(timeJobTime.Value, now);
            }
        }

        if (!includeCron && !includeTimeJobs)
        {
            return (Timeout.InfiniteTimeSpan, []);
        }

        JobExecutionState[] cronFunctions = [];
        JobExecutionState[] timeFunctions = [];

        // A group with no items means the earliest projection is not due yet (or this node lost every advance race):
        // it still carries the wake instant, but there is nothing to claim, so skip the provider round trip.
        if (includeCron && minCronGroup is { Items.Length: > 0 })
        {
            cronFunctions = await _QueueNextCronJobsAsync(minCronGroup.Value, cancellationToken).ConfigureAwait(false);
        }

        if (includeTimeJobs && minTimeJobs.Length > 0)
        {
            timeFunctions = await _QueueNextTimeJobsAsync(minTimeJobs, cancellationToken).ConfigureAwait(false);
        }

        if (cronFunctions.Length == 0 && timeFunctions.Length == 0)
        {
            return (timeRemaining, []);
        }

        if (cronFunctions.Length == 0)
        {
            return (timeRemaining, timeFunctions);
        }

        if (timeFunctions.Length == 0)
        {
            return (timeRemaining, cronFunctions);
        }

        var merged = new JobExecutionState[cronFunctions.Length + timeFunctions.Length];
        cronFunctions.AsSpan().CopyTo(merged.AsSpan(0, cronFunctions.Length));
        timeFunctions.AsSpan().CopyTo(merged.AsSpan(cronFunctions.Length, timeFunctions.Length));

        return (timeRemaining, merged);
    }

    private static TimeSpan _SafeRemaining(DateTime target, DateTime now)
    {
        var remaining = target - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private async Task<JobExecutionState[]> _QueueNextTimeJobsAsync(
        TimeJobEntity[] minTimeJobs,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<JobExecutionState>();

        await foreach (var updatedTimeJob in persistenceProvider.QueueTimeJobsAsync(minTimeJobs, cancellationToken))
        {
            results.Add(_BuildQueuedTimeJobContext(updatedTimeJob));

            await notificationHubSender.UpdateTimeJobNotifyAsync(updatedTimeJob).ConfigureAwait(false);
        }

        return [.. results];
    }

    private JobExecutionState _BuildQueuedTimeJobContext(TimeJobEntity timeJob)
    {
        var context = new JobExecutionState
        {
            FunctionName = timeJob.Function,
            JobId = timeJob.Id,
            Type = JobType.TimeJob,
            Retries = timeJob.Retries,
            RetryCount = timeJob.RetryCount,
            RetryIntervals = timeJob.RetryIntervals,
            TenantId = timeJob.TenantId,
            ParentId = timeJob.ParentId,
            ExecutionTime = timeJob.ExecutionTime ?? timeProvider.GetUtcNow().UtcDateTime,
        };

        // The provider already hydrated the tree bounded to MaxChainDepth (U3); recurse the whole thing so a chain
        // deeper than the grandchild level is executed with each descendant's own RunCondition/RetryCount intact
        // (omitting RetryCount here would reset the retry budget after restart — docs/solutions precedent).
        foreach (var child in timeJob.Children)
        {
            context.TimeJobChildren.Add(_BuildQueuedTimeJobChildContext(child));
        }

        return context;
    }

    private static JobExecutionState _BuildQueuedTimeJobChildContext(TimeJobEntity child)
    {
        var childContext = new JobExecutionState
        {
            FunctionName = child.Function,
            JobId = child.Id,
            Type = JobType.TimeJob,
            Retries = child.Retries,
            RetryCount = child.RetryCount,
            TenantId = child.TenantId,
            RetryIntervals = child.RetryIntervals,
            ParentId = child.ParentId,
            RunCondition = child.RunCondition ?? RunCondition.OnAnyCompletedStatus,
        };

        foreach (var grandChild in child.Children)
        {
            childContext.TimeJobChildren.Add(_BuildQueuedTimeJobChildContext(grandChild));
        }

        return childContext;
    }

    private async Task<JobExecutionState[]> _QueueNextCronJobsAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) minCronJob,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<JobExecutionState>();

        await foreach (
            var occurrence in persistenceProvider
                .QueueCronJobOccurrencesAsync(minCronJob, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(
                new JobExecutionState
                {
                    ParentId = occurrence.CronJobId,
                    FunctionName = occurrence.CronJob.Function,
                    JobId = occurrence.Id,
                    Type = JobType.CronJobOccurrence,
                    Retries = occurrence.CronJob.Retries,
                    RetryCount = occurrence.RetryCount,
                    RetryIntervals = occurrence.CronJob.RetryIntervals,
                    ExecutionTime = occurrence.ExecutionTime,
                    // R23 site 1 of 2 (normal + recovery dispatch).
                    RecoveredFromUtc = occurrence.RecoveredFromUtc,
                }
            );

            if (occurrence.CreatedAt == occurrence.UpdatedAt && notificationHubSender != null)
            {
                await notificationHubSender
                    .AddCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                    .ConfigureAwait(false);
            }
            else if (notificationHubSender != null)
            {
                await notificationHubSender
                    .UpdateCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                    .ConfigureAwait(false);
            }
        }

        return [.. results];
    }

    // Bounds the projection read. The scheduler wants the earliest instant and whatever ties it, not a page of work;
    // anything beyond this lands on the following wake. Sized well above any realistic same-instant tie count.
    private const int _MaxCronDispatchCandidates = 64;

    // Concurrent advances per wave. Sized so one wave covers a single advance's round-trip latency without letting a
    // cluster open (nodes x tie-group) connections at once; the pool, not the CPU, is the scarce resource here.
    private const int _MaxAdvanceConcurrency = 8;

    private async Task<(DateTime Key, JobManagerDispatchContext[] Items)?> _GetEarliestCronJobGroupAsync(
        CancellationToken cancellationToken = default
    )
    {
        // The occurrence read no longer derives its filter from the definition read (an empty id set searches every
        // definition per the provider contract), so the two are independent and overlap instead of serializing —
        // matching how GetNextJobs already overlaps its cron and time-job reads. Both are uncached by construction,
        // so serializing them would cost a guaranteed extra round trip on every wake on every node.
        var candidatesTask = persistenceProvider.GetEarliestCronDispatchCandidatesAsync(
            _MaxCronDispatchCandidates,
            cancellationToken
        );
        var earliestOccurrenceTask = persistenceProvider.GetEarliestAvailableCronOccurrenceAsync([], cancellationToken);

        await Task.WhenAll(candidatesTask, earliestOccurrenceTask).ConfigureAwait(false);

        var candidates = await candidatesTask.ConfigureAwait(false);
        var earliestAvailableCronOccurrence = await earliestOccurrenceTask.ConfigureAwait(false);

        return await _EarliestCronJobGroupAsync(candidates, earliestAvailableCronOccurrence, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns the indexed projection read into the group to dispatch. Only definitions the STORE considers due are
    /// advanced, and only an advanced definition has its expression evaluated — a definition that is not due costs an
    /// index entry and nothing more.
    /// </summary>
    private async Task<(DateTime Next, JobManagerDispatchContext[] Items)?> _EarliestCronJobGroupAsync(
        CronDispatchCandidates? candidates,
        CronJobOccurrenceEntity<TCronJob> earliestStored,
        CancellationToken cancellationToken
    )
    {
        DateTime? wakeInstant = null;
        DateTime? dispatchInstant = null;
        List<JobManagerDispatchContext>? dispatched = null;
        var storedConsumed = false;

        if (candidates is { Candidates.Count: > 0 })
        {
            // The projection IS the wake instant. Nothing is recomputed here, which is the point: the store already
            // decided when this definition next comes up.
            var earliestProjection = candidates.Candidates[0].NextDueUtc;
            wakeInstant = earliestProjection;

            // Decide which group wins BEFORE advancing anything. The advance commits durable state: a watermark moved
            // past an instant this method then declines to return is an occurrence nothing will ever materialize, and
            // slice 1 has no recovery path to re-derive it. An already-materialized occurrence that sorts strictly
            // earlier wins outright, so in that case nothing advances and the projection waits for the next wake.
            var storedWinsOutright = earliestStored is not null && earliestStored.ExecutionTime < earliestProjection;

            // Due-ness compares two values from one server snapshot, so it is the store's decision, not this node's.
            // The advance re-asserts it atomically, so this comparison selects work rather than authorizing it.
            if (!storedWinsOutright && earliestProjection <= candidates.StoreUtcNow)
            {
                // Ordered by projection, so the tie group is a prefix.
                var tieGroup = candidates.Candidates.TakeWhile(x => x.NextDueUtc == earliestProjection).ToArray();

                // Pair each candidate with the already-materialized occurrence it reuses BEFORE any I/O starts. This
                // is a pure comparison, and resolving it up front is what lets the advances run concurrently without
                // racing on `storedConsumed`.
                //
                // R6: a row already sitting at this instant is REUSED, not duplicated. Carrying it as
                // NextCronOccurrence makes the claim path take the existing row while the watermark still advances
                // past the instant — skipping the advance instead would leave the definition due forever.
                var reuse = new NextCronOccurrence?[tieGroup.Length];
                for (var index = 0; index < tieGroup.Length; index++)
                {
                    if (
                        earliestStored is not null
                        && earliestStored.CronJobId == tieGroup[index].CronJobId
                        && earliestStored.ExecutionTime == tieGroup[index].NextDueUtc
                    )
                    {
                        reuse[index] = new NextCronOccurrence(earliestStored.Id, earliestStored.CreatedAt)
                        {
                            RecoveredFromUtc = earliestStored.RecoveredFromUtc,
                        };
                        storedConsumed = true;
                    }
                }

                // Each candidate targets a disjoint row fenced by its own watermark/revision CAS, so concurrent
                // advances across different definitions never contend and the exactly-one-winner guarantee is
                // unchanged. Serializing them cost 815ms for a 64-definition group against a real PostgreSQL
                // container (12.7ms each); running them in bounded waves cut that to 127ms. That is not a
                // micro-optimization on this path — a wake stalls the whole node's scheduling loop, and the first
                // wake after this feature migrates in is exactly a maximal tie group, because every legacy row shares
                // the same unset projection. The bound keeps an N-node cluster from opening N x group-size
                // connections at once.
                for (var offset = 0; offset < tieGroup.Length; offset += _MaxAdvanceConcurrency)
                {
                    var waveLength = Math.Min(_MaxAdvanceConcurrency, tieGroup.Length - offset);
                    var wave = new Task<JobManagerDispatchContext?>[waveLength];

                    for (var index = 0; index < waveLength; index++)
                    {
                        var candidate = tieGroup[offset + index];
                        var existing = reuse[offset + index];

                        // R9: a definition with no position yet — seeded before this field existed, or created by a
                        // path that did not set it — is initialized from the CREATION rule (watermark at the store's
                        // instant) and never from its occurrence history. That is what makes an upgrade unable to
                        // replay a backlog: an unset watermark sorts first and would otherwise look infinitely behind.
                        wave[index] =
                            candidate.NextDueUtc == default
                                ? _InitializeAndSkipDispatchAsync(candidate, candidates.StoreUtcNow, cancellationToken)
                                : _TryAdvanceForDispatchAsync(
                                    candidate,
                                    existing,
                                    candidates.StoreUtcNow,
                                    cancellationToken
                                );
                    }

                    var waveResults = await Task.WhenAll(wave).ConfigureAwait(false);

                    // Appended in candidate order: the claim path preserves the order it is given, and a wave-ordered
                    // result set would make dispatch order depend on completion timing.
                    foreach (var context in waveResults)
                    {
                        if (context is not null)
                        {
                            (dispatched ??= []).Add(context);
                        }
                    }
                }

                if (dispatched is { Count: > 0 })
                {
                    dispatchInstant = earliestProjection;
                }
            }
        }

        if (earliestStored is not null && !storedConsumed)
        {
            var storedTime = earliestStored.ExecutionTime;
            var storedItem = new JobManagerDispatchContext(earliestStored.CronJobId)
            {
                FunctionName = earliestStored.CronJob.Function,
                Expression = earliestStored.CronJob.Expression,
                TimeZoneId = earliestStored.CronJob.TimeZoneId,
                IsPaused = earliestStored.CronJob.IsPaused,
                ScheduleRevision = earliestStored.CronJob.ScheduleRevision,
                Retries = earliestStored.CronJob.Retries,
                RetryIntervals = earliestStored.CronJob.RetryIntervals,
                OnNodeDeath = earliestStored.CronJob.OnNodeDeath,
                NextCronOccurrence = new NextCronOccurrence(earliestStored.Id, earliestStored.CreatedAt)
                {
                    RecoveredFromUtc = earliestStored.RecoveredFromUtc,
                },
            };

            if (dispatchInstant is not null)
            {
                // storedTime < dispatchInstant is unreachable by construction: storedWinsOutright above suppresses the
                // advance entirely in that case, so reaching here means the stored occurrence is at or after the
                // dispatched instant. Same instant merges into the group; later waits for the next wake, keeping its
                // durable row untouched.
                if (storedTime == dispatchInstant.Value)
                {
                    dispatched!.Add(storedItem);
                }

                return (dispatchInstant.Value, dispatched!.ToArray());
            }

            // Nothing advanced this wake, so the stored occurrence is the only thing to claim. The wake instant is
            // still whichever comes first: sleeping all the way to a stored occurrence while a projection falls due
            // sooner would dispatch that projection late by the difference.
            var wakeKey = wakeInstant < storedTime ? wakeInstant.Value : storedTime;

            return (wakeKey, [storedItem]);
        }

        if (dispatchInstant is not null)
        {
            return (dispatchInstant.Value, dispatched!.ToArray());
        }

        // Nothing to claim, but still report the earliest projection so the loop sleeps to it rather than to a
        // recomputed instant. A lost advance race lands here too: the winner moved the projection, so the next wake
        // reads the new one.
        return wakeInstant is null ? null : (wakeInstant.Value, []);
    }

    /// <summary>
    /// Initializes a positionless definition and dispatches nothing this wake, so it can share the advance wave's
    /// result shape. The definition becomes selectable at its real projection on the next wake.
    /// </summary>
    private async Task<JobManagerDispatchContext?> _InitializeAndSkipDispatchAsync(
        CronDispatchCandidate candidate,
        DateTime storeUtcNow,
        CancellationToken cancellationToken
    )
    {
        await _InitializeSchedulePositionAsync(candidate, storeUtcNow, cancellationToken).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Gives a definition its first schedule position, anchored at the store's instant rather than at anything in its
    /// history, so nothing before this moment is ever treated as missed.
    /// </summary>
    /// <remarks>
    /// Uses the same compare-and-advance as ordinary dispatch — the unset watermark IS the observed value — so two
    /// nodes initializing the same definition converge on one position instead of racing.
    /// </remarks>
    private async Task _InitializeSchedulePositionAsync(
        CronDispatchCandidate candidate,
        DateTime storeUtcNow,
        CancellationToken cancellationToken
    )
    {
        var firstOccurrence = cronScheduleCache.GetNextOccurrenceOrDefault(
            candidate.Expression,
            storeUtcNow,
            candidate.TimeZoneId
        );

        await persistenceProvider
            .AdvanceCronScheduleAsync(
                new CronScheduleAdvance
                {
                    CronJobId = candidate.CronJobId,
                    ObservedReconciledThroughUtc = candidate.ReconciledThroughUtc,
                    ExpectedScheduleRevision = candidate.ScheduleRevision,
                    ReconciledThroughUtc = storeUtcNow,
                    NextDueUtc = firstOccurrence ?? DateTime.MaxValue,
                    // Stamped with the position it describes, so the sweep can tell a definition positioned under
                    // current rules from one carrying no record of how it was positioned at all.
                    EvaluationFingerprint = cronScheduleCache.ComputeEvaluationFingerprint(candidate.TimeZoneId),
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a backlog under the definition's recovery policy and, when a run was produced, returns the dispatch
    /// context that will claim it.
    /// </summary>
    /// <remarks>
    /// The watermark lands on the recovery instant under both policies (R20), so the backlog they resolved is never
    /// reconsidered. A schedule whose interval is shorter than the wake latency will legitimately re-enter recovery on
    /// the following wake — that is the correct outcome, not a fault.
    /// </remarks>
    private async Task<JobManagerDispatchContext?> _ApplyRecoveryForDispatchAsync(
        CronDispatchCandidate candidate,
        CronPendingEvaluation pending,
        DateTime earliestMissedUtc,
        DateTime storeUtcNow,
        CancellationToken cancellationToken
    )
    {
        // The projection restarts from the recovery instant, not from the backlog, so nothing already resolved can be
        // selected again.
        var nextAfterRecovery = cronScheduleCache.GetNextOccurrenceOrDefault(
            candidate.Expression,
            storeUtcNow,
            candidate.TimeZoneId
        );

        var recovery = await persistenceProvider
            .ApplyCronRecoveryAsync(
                new CronRecoveryRequest
                {
                    CronJobId = candidate.CronJobId,
                    ObservedReconciledThroughUtc = candidate.ReconciledThroughUtc,
                    ExpectedScheduleRevision = candidate.ScheduleRevision,
                    RecoveredThroughUtc = storeUtcNow,
                    NextDueUtc = nextAfterRecovery ?? DateTime.MaxValue,
                    Policy = candidate.OnMissedRun,
                    EarliestMissedUtc = earliestMissedUtc,
                    CoalescedOccurrenceId = guidGenerator.Create(),
                    OnNodeDeath = candidate.OnNodeDeath,
                    OperationTimeUtc = timeProvider.GetUtcNow(),
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (recovery is null)
        {
            // Another node recovered this backlog first. Ordinary on a cluster; nothing was written by this node.
            return null;
        }

        _ResolveInstrumentation()
            ?.LogCronRecoveryApplied(
                candidate.CronJobId,
                candidate.FunctionName,
                candidate.OnMissedRun,
                pending.PendingCount,
                pending.CountSaturated,
                earliestMissedUtc,
                pending.LatestPendingUtc ?? earliestMissedUtc,
                recovery.SkippedOccurrenceCount
            );

        if (recovery.CoalescedRun is null)
        {
            // Skip materialized nothing, or coalesce found the earliest missed instant already occupied by a row that
            // is running or has finished. Either way there is nothing for this wake to claim.
            return null;
        }

        return new JobManagerDispatchContext(candidate.CronJobId)
        {
            FunctionName = candidate.FunctionName,
            Expression = candidate.Expression,
            TimeZoneId = candidate.TimeZoneId,
            IsPaused = false,
            ScheduleRevision = candidate.ScheduleRevision,
            Retries = candidate.Retries,
            RetryIntervals = candidate.RetryIntervals,
            OnNodeDeath = candidate.OnNodeDeath,
            NextCronOccurrence = new NextCronOccurrence(recovery.CoalescedRun.Id, recovery.CoalescedRun.CreatedAt)
            {
                RecoveredFromUtc = recovery.CoalescedRun.RecoveredFromUtc,
            },
        };
    }

    /// <summary>
    /// Advances one due definition's watermark and, when it wins the fence, returns the dispatch context for the
    /// instant it just claimed responsibility for.
    /// </summary>
    private async Task<JobManagerDispatchContext?> _TryAdvanceForDispatchAsync(
        CronDispatchCandidate candidate,
        NextCronOccurrence? existingOccurrence,
        DateTime storeUtcNow,
        CancellationToken cancellationToken
    )
    {
        // Misfire check before ordinary dispatch. A definition whose watermark fell behind must not be dispatched one
        // tick at a time — that would replay the whole backlog occurrence by occurrence, which is the behavior the
        // recovery policies exist to replace.
        var pending = cronScheduleCache.EvaluatePending(
            candidate.Expression,
            candidate.TimeZoneId,
            candidate.ReconciledThroughUtc,
            storeUtcNow,
            candidate.MissedRunGraceSeconds
        );

        if (pending.IsRecovery && pending.EarliestPendingUtc is { } earliestMissed)
        {
            return await _ApplyRecoveryForDispatchAsync(
                    candidate,
                    pending,
                    earliestMissed,
                    storeUtcNow,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        // The only expression evaluation on this path, and only for a definition that is actually due. Deriving a fire
        // time from an expression is tz-database authority and stays here (KTD2); the store owns due-ness and the
        // fence, never the derivation.
        var nextAfterDue = cronScheduleCache.GetNextOccurrenceOrDefault(
            candidate.Expression,
            candidate.NextDueUtc,
            candidate.TimeZoneId
        );

        var advanced = await persistenceProvider
            .AdvanceCronScheduleAsync(
                new CronScheduleAdvance
                {
                    CronJobId = candidate.CronJobId,
                    ObservedReconciledThroughUtc = candidate.ReconciledThroughUtc,
                    ExpectedScheduleRevision = candidate.ScheduleRevision,
                    ReconciledThroughUtc = candidate.NextDueUtc,
                    // A schedule with no further occurrence (an exhausted or unparseable expression) parks its
                    // projection beyond any wake. Leaving the old projection in place would keep the definition
                    // permanently due and spin the scheduler at its minimum sleep forever.
                    NextDueUtc = nextAfterDue ?? DateTime.MaxValue,
                    RequireProjectionDue = true,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (advanced is null)
        {
            // Another node advanced first, the revision moved, the definition was paused, or the store does not
            // consider it due. Every one of those is an ordinary outcome on a multi-node cluster: the loser completes
            // quietly, with no exception and no failed insert.
            return null;
        }

        return new JobManagerDispatchContext(candidate.CronJobId)
        {
            FunctionName = candidate.FunctionName,
            Expression = candidate.Expression,
            TimeZoneId = candidate.TimeZoneId,
            IsPaused = false,
            ScheduleRevision = candidate.ScheduleRevision,
            Retries = candidate.Retries,
            RetryIntervals = candidate.RetryIntervals,
            OnNodeDeath = candidate.OnNodeDeath,
            NextCronOccurrence = existingOccurrence,
        };
    }

    public async Task<JobExecutionState[]> SetTickersInProgress(
        JobExecutionState[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var unifiedFunctionContext = new JobExecutionState { FunctionName = string.Empty }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );

        var cronJobIds = resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();
        var timeJobIds = resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        Guid[] stampedCronJobIds = [];
        Guid[] stampedTimeJobIds = [];

        if (cronJobIds.Length != 0 && timeJobIds.Length != 0)
        {
            var updateCronJobOccurrencesTask = persistenceProvider.UpdateCronJobOccurrencesWithUnifiedContextAsync(
                cronJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            var updateTimeJobsTask = persistenceProvider.UpdateTimeJobsWithUnifiedContextAsync(
                timeJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            await Task.WhenAll(updateCronJobOccurrencesTask, updateTimeJobsTask).ConfigureAwait(false);
            stampedCronJobIds = await updateCronJobOccurrencesTask.ConfigureAwait(false);
            stampedTimeJobIds = await updateTimeJobsTask.ConfigureAwait(false);
        }
        else
        {
            if (cronJobIds.Length != 0)
            {
                stampedCronJobIds = await persistenceProvider
                    .UpdateCronJobOccurrencesWithUnifiedContextAsync(
                        cronJobIds,
                        unifiedFunctionContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (timeJobIds.Length != 0)
            {
                stampedTimeJobIds = await persistenceProvider
                    .UpdateTimeJobsWithUnifiedContextAsync(timeJobIds, unifiedFunctionContext, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var stampedCronJobIdSet = new HashSet<Guid>(stampedCronJobIds);
        var stampedTimeJobIdSet = new HashSet<Guid>(stampedTimeJobIds);
        var stampedResources = resources
            .Where(resource =>
                resource.Type == JobType.TimeJob
                    ? stampedTimeJobIdSet.Contains(resource.JobId)
                    : stampedCronJobIdSet.Contains(resource.JobId)
            )
            .ToArray();

        foreach (var resource in stampedResources)
        {
            resource.Status = JobStatus.InProgress;

            if (resource.Type == JobType.TimeJob)
            {
                await notificationHubSender.UpdateTimeJobFromExecutionState<TTimeJob>(resource).ConfigureAwait(false);
            }
            else
            {
                await notificationHubSender
                    .UpdateCronOccurrenceFromExecutionState<TCronJob>(resource)
                    .ConfigureAwait(false);
            }
        }

        return stampedResources;
    }

    public async Task ReleaseAcquiredResources(
        JobExecutionState[]? resources,
        CancellationToken cancellationToken = default
    )
    {
        if (resources is null)
        {
            await Task.WhenAll(
                    persistenceProvider.ReleaseAcquiredCronJobOccurrencesAsync([], cancellationToken),
                    persistenceProvider.ReleaseAcquiredTimeJobsAsync([], cancellationToken)
                )
                .ConfigureAwait(false);
            return;
        }

        var cronJobIds =
            resources.Length == 0
                ? []
                : resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();

        if (cronJobIds.Length != 0)
        {
            await persistenceProvider
                .ReleaseAcquiredCronJobOccurrencesAsync(cronJobIds, cancellationToken)
                .ConfigureAwait(false);
        }

        var timeJobIds =
            resources.Length == 0 ? [] : resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        if (timeJobIds.Length != 0)
        {
            await persistenceProvider.ReleaseAcquiredTimeJobsAsync(timeJobIds, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> UpdateTickerAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        // #462: propagate the affected-row count so a caller completing a job successfully can detect a fenced-out
        // write (0 rows — the row was reclaimed/terminalized by a sweep after a stall) and flag the divergence.
        int affected;
        if (functionContext.Type == JobType.CronJobOccurrence)
        {
            affected = await persistenceProvider
                .UpdateCronJobOccurrenceAsync(functionContext, cancellationToken)
                .ConfigureAwait(false);
            await notificationHubSender
                .UpdateCronOccurrenceFromExecutionState<TCronJob>(functionContext)
                .ConfigureAwait(false);
        }
        else
        {
            affected = await persistenceProvider
                .UpdateTimeJobAsync(functionContext, cancellationToken)
                .ConfigureAwait(false);
            await notificationHubSender
                .UpdateTimeJobFromExecutionState<TTimeJob>(functionContext)
                .ConfigureAwait(false);
        }

        return affected;
    }

    public async Task<int> RenewLeaseAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    )
    {
        return functionContext.Type == JobType.CronJobOccurrence
            ? await persistenceProvider
                .RenewCronJobOccurrenceLeaseAsync(functionContext.JobId, cancellationToken)
                .ConfigureAwait(false)
            : await persistenceProvider
                .RenewTimeJobLeaseAsync(functionContext.JobId, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<bool> RequestTimeJobCancellationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var accepted = await persistenceProvider
            .RequestTimeJobCancellationAsync(jobId, cancellationToken)
            .ConfigureAwait(false);
        if (!accepted)
        {
            return false;
        }

        try
        {
            await notificationHubSender.CanceledJobNotifyAsync(jobId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogDurableCancellationNotificationFailed(exception, jobId);
        }

        // U5/KTD3: reconcile the cancelled parent's TIMED children through the same reconcile+wake path as the executor,
        // so a released matching child (OnCancelled/OnFailureOrCancelled/OnAnyCompletedStatus) is claimed promptly via
        // RestartIfNeeded instead of waiting for the fallback tick, and non-matching timed children are skipped with
        // their subtree. A running (not-yet-terminal) parent makes this a no-op — the executor reconciles it when it
        // later reaches Cancelled.
        //
        // The cancellation is already committed, so this post-commit reconcile is a recoverable side-effect (the
        // poll-time safety net / set-based sweep reconcile any miss): a failure here must NOT fail the accepted
        // cancellation. CancellationToken.None mirrors the executor's post-commit reconcile — the committed
        // cancellation's follow-up must not be torn down by the caller's token.
        try
        {
            await ApplyParentTerminalRunConditionsAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Non-fatal post-commit side effect: logged, not rethrown (backstops reconcile any miss).
        catch (Exception exception)
        {
            logger.LogTimedChildReconcileAfterCancellationFailed(exception, jobId);
        }
#pragma warning restore ERP022

        return true;
    }

    public Task<bool?> IsTimeJobCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        persistenceProvider.IsTimeJobCancellationRequestedAsync(jobId, cancellationToken);

    public async Task<bool> PauseCronJobAsync(Guid cronJobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var updated = await persistenceProvider
            .PauseCronJobAsync(cronJobId, now, cancellationToken)
            .ConfigureAwait(false);

        return await _PublishAcceptedCronControlAsync(updated, "pause").ConfigureAwait(false);
    }

    public async Task<bool> ResumeCronJobAsync(Guid cronJobId, CancellationToken cancellationToken = default)
    {
        var definition = await persistenceProvider
            .GetCronJobByIdAsync(cronJobId, cancellationToken)
            .ConfigureAwait(false);
        if (definition?.IsPaused != true)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var next = cronScheduleCache.GetNextOccurrenceOrDefault(
            definition.Expression,
            now.UtcDateTime,
            definition.TimeZoneId
        );
        if (next is null)
        {
            return false;
        }

        var occurrence = CronJobOccurrenceFactory.Create(definition, next.Value, now, guidGenerator);
        var updated = await persistenceProvider
            .ResumeCronJobAsync(definition.Id, definition.ScheduleRevision, occurrence, now, cancellationToken)
            .ConfigureAwait(false);

        return await _PublishAcceptedCronControlAsync(updated, "resume").ConfigureAwait(false);
    }

    private async Task<bool> _PublishAcceptedCronControlAsync(TCronJob? updated, string operation)
    {
        if (updated is null)
        {
            return false;
        }

        try
        {
            await notificationHubSender.UpdateCronJobNotifyAsync(updated).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogCronControlNotificationFailed(exception, updated.Id, operation);
        }

        return true;
    }

    public async Task UpdateSkipTimeJobsWithUnifiedContextAsync(
        JobExecutionState[] resources,
        CancellationToken cancellationToken = default
    )
    {
        var now = timeProvider.GetUtcNow();
        var unifiedFunctionContext = new JobExecutionState { FunctionName = string.Empty }
            .SetProperty(x => x.Status, JobStatus.Skipped)
            .SetProperty(x => x.ExecutedAt, now)
            .SetProperty(x => x.ExceptionDetails, ChainRunConditionRules.RunConditionMismatchReason);

        var cronJobIds = resources.Where(x => x.Type == JobType.CronJobOccurrence).Select(x => x.JobId).ToArray();
        var timeJobIds = resources.Where(x => x.Type == JobType.TimeJob).Select(x => x.JobId).ToArray();

        Guid[] skippedCronJobIds = [];
        Guid[] skippedTimeJobIds = [];

        if (cronJobIds.Length != 0 && timeJobIds.Length != 0)
        {
            var updateCronJobOccurrencesTask = persistenceProvider.UpdateCronJobOccurrencesWithUnifiedContextAsync(
                cronJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            var updateTimeJobsTask = persistenceProvider.UpdateTimeJobsWithUnifiedContextAsync(
                timeJobIds,
                unifiedFunctionContext,
                cancellationToken
            );
            await Task.WhenAll(updateCronJobOccurrencesTask, updateTimeJobsTask).ConfigureAwait(false);
            skippedCronJobIds = await updateCronJobOccurrencesTask.ConfigureAwait(false);
            skippedTimeJobIds = await updateTimeJobsTask.ConfigureAwait(false);
        }
        else
        {
            if (cronJobIds.Length != 0)
            {
                skippedCronJobIds = await persistenceProvider
                    .UpdateCronJobOccurrencesWithUnifiedContextAsync(
                        cronJobIds,
                        unifiedFunctionContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (timeJobIds.Length != 0)
            {
                skippedTimeJobIds = await persistenceProvider
                    .UpdateTimeJobsWithUnifiedContextAsync(timeJobIds, unifiedFunctionContext, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var skippedCronJobIdSet = new HashSet<Guid>(skippedCronJobIds);
        var skippedTimeJobIdSet = new HashSet<Guid>(skippedTimeJobIds);
        var skippedResources = resources
            .Where(resource =>
                resource.Type == JobType.TimeJob
                    ? skippedTimeJobIdSet.Contains(resource.JobId)
                    : skippedCronJobIdSet.Contains(resource.JobId)
            )
            .ToArray();

        foreach (var resource in skippedResources)
        {
            resource.ExecutedAt = now;
            resource.Status = JobStatus.Skipped;
            resource.ExceptionDetails = ChainRunConditionRules.RunConditionMismatchReason;
            if (resource.Type == JobType.TimeJob)
            {
                await notificationHubSender.UpdateTimeJobFromExecutionState<TTimeJob>(resource).ConfigureAwait(false);
            }
            else
            {
                await notificationHubSender
                    .UpdateCronOccurrenceFromExecutionState<TCronJob>(resource)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task<T?> GetRequestAsync<T>(Guid jobId, JobType type, CancellationToken cancellationToken = default)
    {
        var request =
            type == JobType.CronJobOccurrence
                ? await persistenceProvider
                    .GetCronJobOccurrenceRequestAsync(jobId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : await persistenceProvider
                    .GetTimeJobRequestAsync(jobId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

        return request == null ? default : JobsHelper.ReadJobRequest<T>(request, serializationOptions);
    }

    public async Task<JobExecutionState[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
    {
        var results = new List<JobExecutionState>();

        await foreach (
            var timedOutTimeJob in persistenceProvider
                .QueueTimedOutTimeJobsAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            results.Add(_BuildQueuedTimeJobContext(timedOutTimeJob));

            await notificationHubSender.UpdateTimeJobNotifyAsync(timedOutTimeJob).ConfigureAwait(false);
        }

        await foreach (
            var timedOutCronJob in persistenceProvider
                .QueueTimedOutCronJobOccurrencesAsync(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            var functionContext = new JobExecutionState
            {
                FunctionName = timedOutCronJob.CronJob.Function,
                JobId = timedOutCronJob.Id,
                Type = JobType.CronJobOccurrence,
                Retries = timedOutCronJob.CronJob.Retries,
                RetryCount = timedOutCronJob.RetryCount,
                RetryIntervals = timedOutCronJob.CronJob.RetryIntervals,
                ParentId = timedOutCronJob.CronJobId,
                ExecutionTime = timedOutCronJob.ExecutionTime,
                // R23 site 2 of 2 (fallback pickup of a stale occurrence — the reclaim-after-restart path, which is
                // exactly where a coalesced run would otherwise forget what it stands for).
                RecoveredFromUtc = timedOutCronJob.RecoveredFromUtc,
            };

            results.Add(functionContext);
            await notificationHubSender
                .UpdateCronOccurrenceFromExecutionState<TCronJob>(functionContext)
                .ConfigureAwait(false);
        }

        return [.. results];
    }

    public async Task<int> RebaseStaleFingerprintsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var knownFingerprints = await _CurrentFingerprintsAsync(cancellationToken).ConfigureAwait(false);

        var candidates = await persistenceProvider
            .GetStaleFingerprintDefinitionsAsync(knownFingerprints, limit, cancellationToken)
            .ConfigureAwait(false);

        if (candidates is null)
        {
            return 0;
        }

        var rebased = 0;

        foreach (var candidate in candidates.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string current;

            try
            {
                current = cronScheduleCache.ComputeEvaluationFingerprint(candidate.TimeZoneId);
            }
            catch (ArgumentException exception)
            {
                // This host cannot resolve the definition's zone — a zone dropped by the very tzdata update that made
                // things stale, or a trimmed container image. Such a definition is permanently a candidate (its
                // fingerprint can never match), so letting the exception escape would abort the sweep on this row and
                // every row after it, on every sweep, forever. Skipping keeps the rest of the sweep working; the
                // definition cannot be repositioned here anyway, because deriving its next occurrence needs the same
                // zone.
                logger.LogUnresolvableCronTimeZone(
                    exception,
                    candidate.CronJobId,
                    candidate.FunctionName,
                    candidate.TimeZoneId ?? "<default>"
                );

                continue;
            }

            if (string.Equals(candidate.EvaluationFingerprint, current, StringComparison.Ordinal))
            {
                // Its rules are in fact current: possible when a zone entered use between the two reads below.
                continue;
            }

            if (await _RebaseAsync(candidate, current, candidates.StoreUtcNow, cancellationToken).ConfigureAwait(false))
            {
                rebased++;
            }
        }

        return rebased;
    }

    /// <summary>
    /// Every fingerprint this evaluator currently produces: one per timezone actually in use, plus the scheduler-wide
    /// fallback.
    /// </summary>
    /// <remarks>
    /// Completeness is what makes the store-side predicate precise, and it is load-bearing rather than an
    /// optimization. A zone missing from this set makes every definition using it match "fingerprint not known" on
    /// every sweep, forever — and because the store applies the batch limit BEFORE the per-candidate confirmation
    /// above, those permanent false positives crowd genuinely stale definitions out of the batch and starve them
    /// indefinitely. The zones in use come from the store rather than from the declared functions because a runtime
    /// definition may name a zone no <c>[JobFunction]</c> mentions.
    /// </remarks>
    private async Task<HashSet<string>> _CurrentFingerprintsAsync(CancellationToken cancellationToken)
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal)
        {
            cronScheduleCache.ComputeEvaluationFingerprint(timeZoneId: null),
        };

        // The one read whose result may legitimately be served from a cache (see IJobPersistenceProvider): a zone that
        // entered use since it was cached only costs one wasted candidate, which the confirmation above absorbs.
        var definitions = await persistenceProvider
            .GetAllCronJobExpressionsAsync(cancellationToken)
            .ConfigureAwait(false);

        var seenZones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (definition.TimeZoneId is not { } zone || !seenZones.Add(zone))
            {
                continue;
            }

            try
            {
                fingerprints.Add(cronScheduleCache.ComputeEvaluationFingerprint(zone));
            }
            catch (ArgumentException)
            {
                // Unresolvable on this host. Contributing nothing is correct — there is no fingerprint that would make
                // definitions in this zone look current — and the per-candidate guard reports it once per sweep with
                // the definition it belongs to, rather than once per zone with no owner.
            }
        }

        return fingerprints;
    }

    /// <summary>
    /// Re-derives one definition's projection under current rules and refreshes its fingerprint, in a single
    /// compare-and-advance so a pause, resume, or edit racing the sweep wins instead of being clobbered.
    /// </summary>
    private async Task<bool> _RebaseAsync(
        CronDispatchCandidate candidate,
        string currentFingerprint,
        DateTime storeUtcNow,
        CancellationToken cancellationToken
    )
    {
        // Derived from the watermark so no interval is skipped, then anchored at or after the store instant so a tick
        // the changed rules moved into the past is NOT replayed as a misfire. That anchoring is the difference between
        // surfacing a rule change and manufacturing a backlog out of one.
        var anchor = candidate.ReconciledThroughUtc > storeUtcNow ? candidate.ReconciledThroughUtc : storeUtcNow;
        var rebased = cronScheduleCache.GetNextOccurrenceOrDefault(candidate.Expression, anchor, candidate.TimeZoneId);

        var advanced = await persistenceProvider
            .AdvanceCronScheduleAsync(
                new CronScheduleAdvance
                {
                    CronJobId = candidate.CronJobId,
                    ObservedReconciledThroughUtc = candidate.ReconciledThroughUtc,
                    ExpectedScheduleRevision = candidate.ScheduleRevision,
                    // The watermark does NOT move: nothing was reconciled, only re-interpreted. Moving it would
                    // silently declare the span between it and now as accounted for.
                    ReconciledThroughUtc = candidate.ReconciledThroughUtc,
                    NextDueUtc = rebased ?? DateTime.MaxValue,
                    EvaluationFingerprint = currentFingerprint,
                    // Never gated on due-ness: a rule change that moves an occurrence earlier is invisible behind the
                    // stale later projection, which is exactly the case this sweep exists for.
                    RequireProjectionDue = false,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (advanced is null)
        {
            // A pause, resume, or edit committed first. Its transition is newer and already carries a correct
            // position, so losing here is the right outcome — the next sweep re-reads whatever it left.
            return false;
        }

        _ResolveInstrumentation()
            ?.LogCronFingerprintRebased(
                candidate.CronJobId,
                candidate.FunctionName,
                candidate.NextDueUtc,
                advanced.NextDueUtc
            );

        return true;
    }

    public async Task MigrateDefinedCronJobs(
        CronSeedDefinition[] cronExpressions,
        CancellationToken cancellationToken = default
    )
    {
        await persistenceProvider.MigrateDefinedCronJobsAsync(cronExpressions, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteJob(Guid jobId, JobType type, CancellationToken cancellationToken = default)
    {
        if (type == JobType.CronJobOccurrence)
        {
            await persistenceProvider.RemoveCronJobsAsync([jobId], cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await persistenceProvider.RemoveTimeJobsAsync([jobId], cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ReleaseDeadNodeResources(string instanceIdentifier, CancellationToken cancellationToken = default)
    {
        var cronOccurrence = persistenceProvider.ReleaseDeadNodeOccurrenceResourcesAsync(
            instanceIdentifier,
            cancellationToken
        );

        var timeJobs = persistenceProvider.ReleaseDeadNodeTimeJobResourcesAsync(instanceIdentifier, cancellationToken);

        await Task.WhenAll(cronOccurrence, timeJobs).ConfigureAwait(false);

        // U5/KTD3: the dead-node sweep terminalizes parents in bulk (MarkFailed/Skip) and reports only counts, so a
        // per-parent reconcile cannot reach them — reconcile every terminal parent's timed children set-based here.
        await _ReconcileAllTerminalTimedChildrenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ReclaimStalledResources(CancellationToken cancellationToken = default)
    {
        var timeJobsTask = persistenceProvider.ReclaimStalledTimeJobsAsync(cancellationToken);
        var cronOccurrencesTask = persistenceProvider.ReclaimStalledCronJobOccurrencesAsync(cancellationToken);

        // WhenAll of two Task<int> yields the results array in one await — concurrent, no double-await, and a double
        // fault surfaces as AggregateException rather than collapsing to the first task's exception.
        var results = await Task.WhenAll(timeJobsTask, cronOccurrencesTask).ConfigureAwait(false);

        // U5/KTD3: the stalled-lease sweep terminalizes parents in bulk (reporting only counts), so reconcile every
        // terminal parent's timed children set-based right after — release matching (re-stamp past-due) / skip
        // non-matching + subtree — mirroring the dead-node path.
        await _ReconcileAllTerminalTimedChildrenAsync(cancellationToken).ConfigureAwait(false);

        return results[0] + results[1];
    }

    public async Task ApplyParentTerminalRunConditionsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default
    )
    {
        // U5/KTD3 per-parent reconcile, invoked after a parent's terminal write committed (executor / cancellation).
        await _ApplyTerminalRunConditionsAndWakeAsync(parentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task _ReconcileAllTerminalTimedChildrenAsync(CancellationToken cancellationToken)
    {
        await _ApplyTerminalRunConditionsAndWakeAsync(parentId: null, cancellationToken).ConfigureAwait(false);
    }

    // Runs the provider reconcile (per-parent when parentId is set, every terminal parent when null) and wakes the
    // scheduler for the earliest released child, if any.
    private async Task _ApplyTerminalRunConditionsAndWakeAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        var earliest = await persistenceProvider
            .ApplyParentTerminalRunConditionsAsync(parentId, cancellationToken)
            .ConfigureAwait(false);

        _WakeSchedulerForReleasedChild(earliest);
    }

    private void _WakeSchedulerForReleasedChild(DateTime? earliestReleasedTime)
    {
        if (earliestReleasedTime is null)
        {
            return;
        }

        // Resolve the host scheduler lazily to break the JobsSchedulerBackgroundService (IJobsHostScheduler) ⇄
        // IInternalJobManager constructor cycle. RestartIfNeeded runs only AFTER the releasing transaction committed
        // (a pre-commit nudge would wake the scheduler into pre-commit state and it would sleep again — KTD3).
        serviceProvider.GetService<IJobsHostScheduler>()?.RestartIfNeeded(earliestReleasedTime);
    }
}

internal static partial class InternalJobsManagerLog
{
    [LoggerMessage(
        EventId = 3212,
        Level = LogLevel.Warning,
        Message = "Durable cancellation for time job {JobId} was committed, but the dashboard notification failed."
    )]
    public static partial void LogDurableCancellationNotificationFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId
    );

    [LoggerMessage(
        EventId = 3213,
        Level = LogLevel.Warning,
        Message = "Cron definition {CronJobId} {Operation} was committed, but the dashboard notification failed."
    )]
    public static partial void LogCronControlNotificationFailed(
        this ILogger logger,
        Exception exception,
        Guid cronJobId,
        string operation
    );

    [LoggerMessage(
        EventId = 3214,
        Level = LogLevel.Debug,
        Message = "Poll-time timed-descendant safety net failed; any stranded timed children will be reconciled by the "
            + "fallback sweep's set-based reconcile instead. Scheduling continues."
    )]
    public static partial void LogTimedChildSafetyNetFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3215,
        Level = LogLevel.Warning,
        Message = "Durable cancellation for time job {JobId} was committed, but the post-commit timed-descendant "
            + "reconcile failed; its timed children will be reconciled by the poll-time safety net / set-based sweep "
            + "instead."
    )]
    public static partial void LogTimedChildReconcileAfterCancellationFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId
    );

    [LoggerMessage(
        EventId = 3216,
        Level = LogLevel.Warning,
        Message = "Cron definition {CronJobId} ({FunctionName}) names time zone '{TimeZoneId}', which this host "
            + "cannot resolve. Its schedule interpretation cannot be re-derived here and it is skipped by the "
            + "fingerprint sweep; the rest of the sweep continues."
    )]
    public static partial void LogUnresolvableCronTimeZone(
        this ILogger logger,
        Exception exception,
        Guid cronJobId,
        string functionName,
        string timeZoneId
    );
}
