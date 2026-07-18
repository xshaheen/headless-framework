// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
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
    private readonly ILogger<JobsSchedulerBackgroundService> _logger;
    private int _started;
    public bool SkipFirstRun { get; set; }
    public bool IsRunning => _started == 1;

    public JobsSchedulerBackgroundService(
        JobsExecutionContext executionContext,
        JobFunctionRegistry functionRegistry,
        JobsExecutionTaskHandler taskHandler,
        JobsTaskScheduler taskScheduler,
        IInternalJobManager internalJobsManager,
        IJobFunctionConcurrencyGate concurrencyGate,
        TimeProvider timeProvider,
        IJobsOwnerIdentity ownerIdentity,
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
        _logger = Argument.IsNotNull(logger);
        _restartThrottle = new RestartThrottleManager(
            () => _schedulerLoopCancellationTokenSource?.Cancel(),
            timeProvider
        );
    }

    public override Task StartAsync(CancellationToken ct)
    {
        if (SkipFirstRun)
        {
            _taskScheduler.Freeze();
            SkipFirstRun = false;
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
                // This is a restart request - release resources and continue loop
                await _internalJobsManager
                    .ReleaseAcquiredResources(_executionContext.Functions, loopToken)
                    .ConfigureAwait(false);
                // Small delay to allow resources to be released
                await _timeProvider.Delay(TimeSpan.FromMilliseconds(100), loopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
            {
                // Host shutdown or local membership loss - release resources and exit
                await _internalJobsManager
                    .ReleaseAcquiredResources(_executionContext.Functions, CancellationToken.None)
                    .ConfigureAwait(false);
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

    private async Task _RunJobsSchedulerAsync(CancellationToken stoppingToken, CancellationToken cancellationToken)
    {
        while (!stoppingToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (_executionContext.Functions.Length != 0)
            {
                foreach (var function in _executionContext.Functions.OrderBy(x => x.CachedPriority))
                {
                    var semaphore = _concurrencyGate.GetSemaphoreOrNull(
                        function.FunctionName,
                        function.CachedMaxConcurrency
                    );

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

                _executionContext.ClearFunctions();
            }

            var (timeRemaining, functions) = await _internalJobsManager
                .GetNextJobs(cancellationToken)
                .ConfigureAwait(false);

            _executionContext.SetFunctions(functions, _functionRegistry);

            TimeSpan sleepDuration;
            if (timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1))
            {
                sleepDuration = TimeSpan.FromDays(1);
                _executionContext.SetNextPlannedOccurrence(dt: null);
                _executionContext.ClearFunctions();
            }
            else
            {
                sleepDuration = timeRemaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeRemaining;
                _executionContext.SetNextPlannedOccurrence(_timeProvider.GetUtcNow().UtcDateTime.Add(sleepDuration));
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

        await _internalJobsManager.ReleaseAcquiredResources([], CancellationToken.None).ConfigureAwait(false);
    }

    public void RestartIfNeeded(DateTime? dateTime)
    {
        if (!dateTime.HasValue)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var nextPlannedOccurrence = _executionContext.GetNextPlannedOccurrence();

        // Restart if:
        // 1. No tasks are currently planned, OR
        // 2. The new task should execute at least 500ms earlier than the currently planned task, OR
        // 3. The new task is already due/overdue (ExecutionTime <= now)
        if (nextPlannedOccurrence == null)
        {
            _restartThrottle.RequestRestart();
            return;
        }

        var newTime = dateTime.Value;
        var threshold = TimeSpan.FromMilliseconds(500);
        var diff = nextPlannedOccurrence.Value - newTime;

        if (newTime <= now || diff > threshold)
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
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _restartThrottle.Dispose();
        _schedulerLoopCancellationTokenSource?.Dispose();
        base.Dispose();
    }
}
