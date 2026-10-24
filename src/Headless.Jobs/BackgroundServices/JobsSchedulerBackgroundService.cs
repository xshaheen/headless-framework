// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Internal;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly CancellationTokenRegistration _hostStoppingRegistration;
    private int _started;
    private int _manualStartConsumed;

    // Completed when a stop has finished draining and writing its releases. Null until the first start, so a host
    // whose scheduler never started has nothing to wait for; replaced on a start after a stop, so a restarted
    // scheduler is waited on again.
    private TaskCompletionSource? _drained;

    // The part of the stop budget kept, on the real clock, for jobs the stop cancels to write their release: the
    // coordination heartbeat's leave budget, capped at half the stop budget so a short budget still lets jobs finish.
    private static readonly TimeSpan _ReleaseWindow = TimeSpan.FromSeconds(5);

    // Store clock minus node clock, as observed on the last poll that reached the store. Zero until the first such
    // poll, which is exactly the assumption the loop made unconditionally before. Written by the loop, read by
    // RestartIfNeeded on arbitrary caller threads, so it moves through Interlocked/Volatile rather than a plain field.
    private long _storeClockOffsetTicks;

    // Real-clock timestamp of the host's stop start, or zero until ApplicationStopping fires. The host stops hosted
    // services one after another, so services registered after Jobs stop first and spend part of the host's shutdown
    // timeout before StopAsync runs here; counting the budget from the host's stop keeps the release window inside the
    // host's deadline.
    private long _hostStopStartedAt;

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
        ILogger<JobsSchedulerBackgroundService> logger,
        IHostApplicationLifetime? hostLifetime = null
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
            () => Volatile.Read(ref _schedulerLoopCancellationTokenSource)?.Cancel(),
            timeProvider
        );

        // Registered here rather than in StartAsync so a scheduler that never started, or one started manually after
        // the host started, still measures from the host's stop.
        _hostStoppingRegistration =
            hostLifetime?.ApplicationStopping.Register(
                static state =>
                    Interlocked.CompareExchange(
                        ref ((JobsSchedulerBackgroundService)state!)._hostStopStartedAt,
                        TimeProvider.System.GetTimestamp(),
                        0
                    ),
                this
            )
            ?? default;
    }

    public override Task StartAsync(CancellationToken ct)
    {
        _ArmDrainSignal();

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
        // Fail-stop: on local membership loss the owner identity's token fires, so the loop exits cleanly
        // instead of spinning on a refused stamp. On the in-memory path this token is None and never fires.
        using var membershipLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _ownerIdentity.MembershipLostToken
        );
        var loopToken = membershipLinkedCts.Token;

        // Activation gate: no dispatch selection before JobsActivationWarmup has drained one stable fingerprint
        // snapshot. The drain runs after the host start, so awaiting the barrier — rather than assuming anything about
        // hosted-service start order — is what keeps the gate intact in every startup order.
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
            // The initializer rejected the configuration and the failure aborts host startup; this loop stays closed
            // rather than selecting under an unverified schedule interpretation.
            _logger.LogJobsSchedulerStoppedOnActivationFailure(activationFailure);

            return;
        }

        while (!loopToken.IsCancellationRequested)
        {
            // The restart timer reads this field from another thread; publish and retire it atomically.
#pragma warning disable CA2000 // False positive: ownership moves to the field, which the finally below exchanges out and disposes.
            var iterationSource = SafeCancellationTokenSource.CreateLinked(loopToken);
#pragma warning restore CA2000
            Volatile.Write(ref _schedulerLoopCancellationTokenSource, iterationSource);

            try
            {
                await _RunJobsSchedulerAsync(loopToken, iterationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (iterationSource.Token.IsCancellationRequested && !loopToken.IsCancellationRequested)
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
                Interlocked.Exchange(ref _schedulerLoopCancellationTokenSource, value: null)?.Dispose();
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
                                    _taskScheduler,
                                    _internalJobsManager,
                                    _taskHandler,
                                    _logger,
                                    function,
                                    isDue: false
                                ),
                                function.CachedPriority,
                                cancellationToken,
                                stoppingToken,
                                semaphore
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

    /// <summary>
    /// Completes when the last started scheduler has stopped, that is once <see cref="StopAsync"/> has drained running
    /// jobs and written their releases, or at once when the scheduler never started.
    /// </summary>
    internal Task WaitUntilDrainedAsync(CancellationToken cancellationToken)
    {
        var drained = Volatile.Read(ref _drained);

        return drained is null ? Task.CompletedTask : drained.Task.WaitAsync(cancellationToken);
    }

    private void _ArmDrainSignal()
    {
        var current = Volatile.Read(ref _drained);
        if (current?.Task.IsCompleted == false)
        {
            return;
        }

        Interlocked.CompareExchange(
            ref _drained,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            current
        );
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Read(ref _drained)?.TrySetResult();
        }
    }

    private async Task _StopCoreAsync(CancellationToken cancellationToken)
    {
        // The stop budget is real elapsed time, whatever clock the app schedules on, so it lines up with the host's own
        // shutdown deadline. It counts from the host's stop start; a stop outside one (a manual stop, or a host without
        // a lifetime) counts from here.
        var hostStopStartedAt = Interlocked.Read(ref _hostStopStartedAt);
        var stopStarted = hostStopStartedAt != 0 ? hostStopStartedAt : TimeProvider.System.GetTimestamp();

        // Draining first: an item still queued in the pool hands its row back instead of starting a job, so the waits
        // below cover only jobs that were already running.
        _taskScheduler.BeginDrain();
        Interlocked.Exchange(ref _started, 0);

        var budget = _schedulerOptions.ShutdownTimeout;
        var halfBudget = budget / 2;
        var releaseWindow = halfBudget < _ReleaseWindow ? halfBudget : _ReleaseWindow;

        // In-flight jobs get the budget, less the release window, to finish and write their own outcome, so a routine
        // deploy is not node death.
        var finishWindow = budget - releaseWindow - TimeProvider.System.GetElapsedTime(stopStarted);
        await _WaitForRunningTasksAsync(finishWindow, cancellationToken).ConfigureAwait(false);

        // Immediate and fallback dispatch do not flow through this service's ExecuteAsync, so they cannot use its
        // stopping token. Cancel the task scheduler's execution lifetime they run on, then let base.StopAsync cancel
        // the scheduler-loop token used by polled work.
        await _taskScheduler.CancelExecutionsAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Every cancelled job now settles its row by its node-death policy, without charging a retry. Returning
        // before those writes land would let the host dispose the store under them, so spend the rest of the budget
        // waiting for them; a job that ignores cancellation past it falls to reclaim like a crash.
        var remaining = budget - TimeProvider.System.GetElapsedTime(stopStarted);
        await _WaitForRunningTasksAsync(remaining, cancellationToken).ConfigureAwait(false);
    }

    private async Task _WaitForRunningTasksAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _taskScheduler
                .WaitForRunningTasksAsync()
                .WaitAsync(timeout, TimeProvider.System, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // This phase of the stop budget ran out.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host's own shutdown deadline arrived first.
        }
    }

    public override void Dispose()
    {
        _hostStoppingRegistration.Dispose();
        _restartThrottle.Dispose();
        Interlocked.Exchange(ref _schedulerLoopCancellationTokenSource, value: null)?.Dispose();
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
