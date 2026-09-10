// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs.BackgroundServices;

internal sealed class JobsSchedulerBackgroundService : BackgroundService, IJobsHostScheduler
{
    private readonly RestartThrottleManager _restartThrottle;
    private readonly IInternalJobManager _internalJobsManager;
    private readonly JobsExecutionContext _executionContext;
    private readonly JobFunctionRegistry _functionRegistry;
    private SafeCancellationTokenSource? _schedulerLoopCancellationTokenSource;

#pragma warning disable CA2213 // Justification = "Owned by the DI container as a singleton; disposed on host shutdown."
    private readonly JobsTaskScheduler _taskScheduler;
#pragma warning restore CA2213
    private readonly JobsExecutionTaskHandler _taskHandler;
    private readonly IJobFunctionConcurrencyGate _concurrencyGate;
    private readonly TimeProvider _timeProvider;
    private readonly IJobsOwnerIdentity _ownerIdentity;
    private readonly SchedulerOptionsBuilder _schedulerOptions;
    private readonly JobsActivationBarrier _activationBarrier;
    private readonly ILogger<JobsSchedulerBackgroundService> _logger;
    private int _started;
    private int _manualStartConsumed;

    // Store clock minus node clock, as observed on the last poll that reached the store. Zero until the first such
    // poll, which is exactly the assumption the loop made unconditionally before. Written by the loop, read by
    // RestartIfNeeded on arbitrary caller threads, so it moves through Interlocked/Volatile rather than a plain field.
    private long _storeClockOffsetTicks;

    public bool IsRunning => _started == 1;

    /// <summary>
    /// This node's best estimate of the store's clock: the node clock shifted by the offset observed on the last poll.
    /// Both clocks advance at the same rate between polls, so the estimate stays as good as that observation.
    /// </summary>
    private DateTime _StoreUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime.AddTicks(Interlocked.Read(ref _storeClockOffsetTicks));
    }

    public JobsSchedulerBackgroundService(
        JobsExecutionContext executionContext,
        JobFunctionRegistry functionRegistry,
        JobsExecutionTaskHandler taskHandler,
        JobsTaskScheduler taskScheduler,
        IInternalJobManager internalJobsManager,
        IJobFunctionConcurrencyGate concurrencyGate,
        TimeProvider timeProvider,
        IJobsOwnerIdentity ownerIdentity,
        SchedulerOptionsBuilder schedulerOptions,
        JobsActivationBarrier activationBarrier,
        ILogger<JobsSchedulerBackgroundService> logger
    )
    {
        _executionContext = Argument.IsNotNull(executionContext);
        _functionRegistry = Argument.IsNotNull(functionRegistry);
        _taskHandler = Argument.IsNotNull(taskHandler);
        _taskScheduler = Argument.IsNotNull(taskScheduler);
        _internalJobsManager = Argument.IsNotNull(internalJobsManager);
        _concurrencyGate = Argument.IsNotNull(concurrencyGate);
        _timeProvider = Argument.IsNotNull(timeProvider);
        _ownerIdentity = Argument.IsNotNull(ownerIdentity);
        _schedulerOptions = Argument.IsNotNull(schedulerOptions);
        _activationBarrier = Argument.IsNotNull(activationBarrier);
        _logger = Argument.IsNotNull(logger);
        _restartThrottle = new RestartThrottleManager(
            () => _schedulerLoopCancellationTokenSource?.Cancel(),
            timeProvider
        );
    }

    public override Task StartAsync(CancellationToken ct)
    {
        // JobsStartMode.Manual is consumed exactly once, on the host's first start of this service: freeze the pool
        // and return without running the loop, so nothing dispatches until an explicit IJobsHostScheduler.StartAsync.
        // The mode is read from THIS service's own configuration rather than pushed in beforehand by
        // JobsInitializationHostedService — a pushed flag only works when the initializer starts first, which
        // HostOptions.ServicesStartConcurrently does not guarantee. Leaving the pool frozen also idles the fallback
        // loop, which gates on pool state, so manual mode suppresses re-dispatch in every startup order.
        if (
            _schedulerOptions.StartMode == JobsStartMode.Manual
            && Interlocked.CompareExchange(ref _manualStartConsumed, 1, 0) == 0
        )
        {
            _taskScheduler.Freeze();

            return Task.CompletedTask;
        }

        _taskScheduler.Resume();
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fail-stop (R9): on local membership loss the owner identity's token fires, so the loop exits cleanly
        // instead of spinning on a refused stamp. On the in-memory path this token is None and never fires.
        using var membershipLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _ownerIdentity.MembershipLostToken
        );
        var loopToken = membershipLinkedCts.Token;

        // Activation gate: no dispatch selection before JobsInitializationHostedService has drained one stable
        // fingerprint snapshot. Awaiting the barrier — rather than assuming hosted services start sequentially in
        // registration order — is what keeps the gate intact under HostOptions.ServicesStartConcurrently.
        Exception? activationFailure;
        try
        {
            activationFailure = await _activationBarrier.WaitAsync(loopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Same permanent-exit diagnostic the loop below emits: under StopMembershipOnly the process keeps running
            // with no scheduler, and losing membership while still parked here must not be the one silent path.
            _LogLoopStopReason(stoppingToken);

            return;
        }

        if (activationFailure is not null)
        {
            // The failure already propagated out of the initializer's StartAsync and aborts host startup; this loop
            // stays closed rather than selecting under an unverified schedule interpretation.
            _logger.LogJobsSchedulerStoppedOnActivationFailure(activationFailure);

            return;
        }

        while (!loopToken.IsCancellationRequested)
        {
            _schedulerLoopCancellationTokenSource = SafeCancellationTokenSource.CreateLinked(loopToken);

            try
            {
                await _RunJobsSchedulerAsync(loopToken, _schedulerLoopCancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_schedulerLoopCancellationTokenSource.Token.IsCancellationRequested
                    && !loopToken.IsCancellationRequested
                )
            {
                // This is a restart request - release this wake's claims and continue loop. Explicit ids only,
                // and only when the context actually holds claims: the empty form releases every Queued row this
                // owner holds, including rows whose admissions from earlier ticks are still parked in the pool.
                if (_executionContext.Functions.Length != 0)
                {
                    await _internalJobsManager
                        .ReleaseAcquiredResources(_executionContext.Functions, loopToken)
                        .ConfigureAwait(false);
                }

                // Small delay to allow resources to be released
                await _timeProvider.Delay(TimeSpan.FromMilliseconds(100), loopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
            {
                // Host shutdown or local membership loss - release this wake's claims and exit. Same explicit-ids
                // rule as the restart path; rows whose admissions are already parked in the pool are recovered by
                // the lease-lapse sweep rather than released out from under a possibly-running admission.
                if (_executionContext.Functions.Length != 0)
                {
                    await _internalJobsManager
                        .ReleaseAcquiredResources(_executionContext.Functions, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                _LogLoopStopReason(stoppingToken);

                break;
            }
            catch (Exception ex)
            {
                await _ReleaseAllResourcesAsync(ex).ConfigureAwait(false);
                // Continue running - don't exit the scheduler loop on exceptions
                // Add a small delay to prevent tight loop if errors persist
                await _timeProvider.Delay(TimeSpan.FromSeconds(1), loopToken).ConfigureAwait(false);
            }
            finally
            {
                _executionContext.ClearFunctions();
                _schedulerLoopCancellationTokenSource?.Dispose();
                _schedulerLoopCancellationTokenSource = null;
            }
        }
    }

    // Either exit is permanent for the host's lifetime — under StopMembershipOnly the process keeps running without a
    // scheduler — so the cause must be visible in logs. Shared by the loop's cancellation arm and the activation wait
    // so the two can never drift into one of them exiting silently.
    private void _LogLoopStopReason(CancellationToken stoppingToken)
    {
        if (_ownerIdentity.MembershipLostToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogJobsSchedulerStoppedOnMembershipLoss();
        }
        else
        {
            _logger.LogJobsSchedulerStoppedOnShutdown();
        }
    }

    private async Task _RunJobsSchedulerAsync(CancellationToken stoppingToken, CancellationToken cancellationToken)
    {
        while (!stoppingToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Shutdown drain (StopAsync freezes the pool, then waits for running tasks while this loop is still
            // alive) or manual-start freeze: the pool accepts no admissions, so claiming or queuing would only
            // throw into the fault path — which releases every Queued row this owner holds, un-claiming the very
            // backlog the drain is waiting beside, once per second for the whole drain window. Mirror the
            // fallback service's guard instead: release this wake's parked claims explicitly and idle until the
            // loop token fires or the pool resumes. A released row's former owner fails the ownership predicate
            // on the Queued→InProgress transition, so a racing admission drops it rather than double-running.
            if (_taskScheduler.IsFrozen || _taskScheduler.IsDisposed)
            {
                var parked = _executionContext.Functions;
                if (parked.Length != 0)
                {
                    await _internalJobsManager
                        .ReleaseAcquiredResources(parked, cancellationToken)
                        .ConfigureAwait(false);
                    _executionContext.ClearFunctions();
                }

                await _timeProvider.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (_executionContext.Functions.Length != 0)
            {
                var frozenMidDispatch = false;

                foreach (var function in _executionContext.Functions.OrderBy(x => x.CachedPriority.DispatchRank()))
                {
                    var semaphore = _concurrencyGate.GetSemaphoreOrNull(
                        function.FunctionName,
                        function.CachedMaxConcurrency
                    );

                    try
                    {
                        await _taskScheduler
                            .QueueAsync(
                                JobsAdmissionWorkItem.Create(
                                    _internalJobsManager,
                                    _taskHandler,
                                    _logger,
                                    semaphore,
                                    function,
                                    isDue: false
                                ),
                                function.CachedPriority,
                                cancellationToken,
                                stoppingToken
                            )
                            .ConfigureAwait(false);
                    }
                    catch (InvalidOperationException) when (_taskScheduler.IsFrozen || _taskScheduler.IsDisposed)
                    {
                        // Freeze raced the pre-check; keep the remaining functions in the context so the guard
                        // above releases them on the next pass.
                        frozenMidDispatch = true;
                        break;
                    }
                }

                if (frozenMidDispatch)
                {
                    continue;
                }

                _executionContext.ClearFunctions();
            }

            var (wake, functions) = await _internalJobsManager.GetNextJobs(cancellationToken).ConfigureAwait(false);

            _executionContext.SetFunctions(functions, _functionRegistry);

            // THE one place a node clock meets a store clock (see JobsWakeSchedule). Every due instant in this
            // subsystem is a store instant; this offset is what lets a restart request expressed in that domain be
            // compared against a wake this node is sleeping towards. It is refreshed on every poll that reached the
            // store, and left alone otherwise so a read that observed nothing cannot silently reset a real skew to
            // zero.
            if (wake.StoreUtcNow is { } observedStoreUtcNow)
            {
                Interlocked.Exchange(
                    ref _storeClockOffsetTicks,
                    (observedStoreUtcNow - _timeProvider.GetUtcNow().UtcDateTime).Ticks
                );
            }

            var timeRemaining = wake.Remaining;

            TimeSpan sleepDuration;
            if (timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1))
            {
                sleepDuration = TimeSpan.FromDays(1);
                _executionContext.SetNextPlannedOccurrence(dt: null);

                // GetNextJobs already claimed these far-future rows (Queued + lease). Nothing dispatches them for
                // at least a day — far past any lease — so release them instead of leaving claimed rows to churn
                // through lease-lapse recovery on every wake. Explicit ids only: the empty form would also release
                // rows whose admissions from earlier ticks are still parked in the pool.
                var farFutureClaims = _executionContext.Functions;
                if (farFutureClaims.Length != 0)
                {
                    await _internalJobsManager
                        .ReleaseAcquiredResources(farFutureClaims, cancellationToken)
                        .ConfigureAwait(false);
                }

                _executionContext.ClearFunctions();
            }
            else
            {
                sleepDuration = timeRemaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeRemaining;

                // Recorded in the STORE's domain, not this node's: RestartIfNeeded compares incoming due instants —
                // which are store instants — against it. Adding a store-derived duration to this node's clock instead
                // would shift the planned wake by the node's skew, and a job enqueued for a time before that wake
                // would look later than it and fail to interrupt the sleep.
                _executionContext.SetNextPlannedOccurrence(_StoreUtcNow().Add(sleepDuration));
            }

            _executionContext.NotifyCoreAction?.Invoke(
                _executionContext.GetNextPlannedOccurrence(),
                CoreNotifyActionType.NotifyNextOccurence
            );

            await _timeProvider.Delay(sleepDuration, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _ReleaseAllResourcesAsync(Exception ex)
    {
        if (ex != null && _executionContext.NotifyCoreAction != null)
        {
            _executionContext.NotifyCoreAction(ex.ToString(), CoreNotifyActionType.NotifyHostExceptionMessage);
        }

        // The claimed-but-undispatched rows live in the execution context, exactly as in the two cancellation arms
        // above. Passing [] here released nothing at all (ReleaseAcquiredResources skips both provider calls for an
        // empty non-null array), so every claim held at the time of an unexpected failure stayed leased until it
        // lapsed. Rows abandoned mid-claim-enumeration are released by the manager at the source instead — they never
        // reach an execution context.
        await _internalJobsManager
            .ReleaseAcquiredResources(_executionContext.Functions, CancellationToken.None)
            .ConfigureAwait(false);
    }

    public void RestartIfNeeded(DateTime? dueAtStoreUtc)
    {
        if (!dueAtStoreUtc.HasValue)
        {
            return;
        }

        // Every value in this comparison is a STORE instant: the incoming due time, the planned wake the loop
        // recorded, and this node's estimate of store now. Comparing a store-domain due time against a node-domain
        // wake is what let a skewed node sleep past work that was brought forward (#818) — the conversion happens
        // here, once, and never at a call site.
        var storeNow = _StoreUtcNow();
        var nextPlannedOccurrence = _executionContext.GetNextPlannedOccurrence();

        // Restart if:
        // 1. No tasks are currently planned, OR
        // 2. The new task should execute at least 500ms earlier than the currently planned task, OR
        // 3. The new task is already due/overdue (due time <= store now)
        if (nextPlannedOccurrence == null)
        {
            _restartThrottle.RequestRestart();
            return;
        }

        var newTime = dueAtStoreUtc.Value;
        var threshold = TimeSpan.FromMilliseconds(500);
        var diff = nextPlannedOccurrence.Value - newTime;

        if (newTime <= storeNow || diff > threshold)
        {
            _restartThrottle.RequestRestart();
        }
    }

    public void Restart()
    {
        _restartThrottle.RequestRestart();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _taskScheduler.Freeze();
        Interlocked.Exchange(ref _started, 0);

        // Bounded drain BEFORE cancelling either execution lifetime: in-flight jobs get the host's
        // remaining shutdown budget to complete and write their terminal status, so a routine deploy stops being
        // indistinguishable from node death (previously every running job was cancelled with no terminal write,
        // sat out its lease, and was resolved by OnNodeDeath — including false Failed records for MarkFailed
        // jobs that never misbehaved). cancellationToken fires when the host's ShutdownTimeout expires;
        // stragglers are then cancelled cooperatively by base.StopAsync and recovered by the existing sweeps.
        try
        {
            await _taskScheduler.WaitForRunningTasksAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown budget exhausted — fall through to cooperative cancellation of the remaining work.
        }

        // Immediate dispatch does not flow through BackgroundService.ExecuteAsync, so it cannot use the base
        // stopping token. Cancel the task scheduler's execution lifetime after the same drain boundary, then let
        // base.StopAsync cancel the scheduler-loop token used by polled and fallback work.
        await _taskScheduler.CancelExecutionsAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _restartThrottle.Dispose();
        _schedulerLoopCancellationTokenSource?.Dispose();
        base.Dispose();
    }
}

internal static partial class JobsSchedulerBackgroundServiceLog
{
    [LoggerMessage(
        EventId = 3220,
        Level = LogLevel.Warning,
        Message = "Jobs scheduler loop stopped because local coordination membership was lost; "
            + "no jobs will be claimed or dispatched by this node until the host restarts."
    )]
    public static partial void LogJobsSchedulerStoppedOnMembershipLoss(this ILogger logger);

    [LoggerMessage(
        EventId = 3221,
        Level = LogLevel.Information,
        Message = "Jobs scheduler loop stopped for host shutdown."
    )]
    public static partial void LogJobsSchedulerStoppedOnShutdown(this ILogger logger);

    [LoggerMessage(
        EventId = 3222,
        Level = LogLevel.Warning,
        Message = "Jobs scheduler loop stopped because startup activation failed; no jobs will be claimed or "
            + "dispatched by this node. Host startup fails with the underlying error."
    )]
    public static partial void LogJobsSchedulerStoppedOnActivationFailure(this ILogger logger, Exception exception);
}
