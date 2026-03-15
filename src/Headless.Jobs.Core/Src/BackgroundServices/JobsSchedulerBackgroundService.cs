using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.BackgroundServices;

internal class JobsSchedulerBackgroundService : BackgroundService, IJobsHostScheduler
{
    private readonly RestartThrottleManager _restartThrottle;
    private readonly IInternalJobManager _internalJobsManager;
    private readonly JobsExecutionContext _executionContext;
    private SafeCancellationTokenSource? _schedulerLoopCancellationTokenSource;
    private readonly JobsTaskScheduler _taskScheduler;
    private readonly JobsExecutionTaskHandler _taskHandler;
    private readonly IJobFunctionConcurrencyGate _concurrencyGate;
    private int _started;
    public bool SkipFirstRun { get; set; }
    public bool IsRunning => _started == 1;

    public JobsSchedulerBackgroundService(
        JobsExecutionContext executionContext,
        JobsExecutionTaskHandler taskHandler,
        JobsTaskScheduler taskScheduler,
        IInternalJobManager internalJobsManager,
        IJobFunctionConcurrencyGate concurrencyGate
    )
    {
        _executionContext = executionContext;
        _taskHandler = taskHandler;
        _taskScheduler = taskScheduler;
        _internalJobsManager =
            internalJobsManager ?? throw new ArgumentNullException(nameof(internalJobsManager));
        _concurrencyGate = concurrencyGate;
        _restartThrottle = new RestartThrottleManager(() => _schedulerLoopCancellationTokenSource?.Cancel());
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
        while (!stoppingToken.IsCancellationRequested)
        {
            _schedulerLoopCancellationTokenSource = SafeCancellationTokenSource.CreateLinked(stoppingToken);

            try
            {
                await _RunJobsSchedulerAsync(stoppingToken, _schedulerLoopCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
                when (_schedulerLoopCancellationTokenSource.Token.IsCancellationRequested
                    && !stoppingToken.IsCancellationRequested
                )
            {
                // This is a restart request - release resources and continue loop
                await _internalJobsManager.ReleaseAcquiredResources(_executionContext.Functions, stoppingToken);
                // Small delay to allow resources to be released
                await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Application is shutting down - release resources and exit
                await _internalJobsManager.ReleaseAcquiredResources(
                    _executionContext.Functions,
                    CancellationToken.None
                );
                break;
            }
            catch (Exception ex)
            {
                await _ReleaseAllResourcesAsync(ex);
                // Continue running - don't exit the scheduler loop on exceptions
                // Add a small delay to prevent tight loop if errors persist
                await Task.Delay(1000, stoppingToken);
            }
            finally
            {
                _executionContext.SetFunctions(null);
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
                await _internalJobsManager.SetTickersInProgress(_executionContext.Functions, cancellationToken);

                foreach (var function in _executionContext.Functions.OrderBy(x => x.CachedPriority))
                {
                    var semaphore = _concurrencyGate.GetSemaphoreOrNull(
                        function.FunctionName,
                        function.CachedMaxConcurrency
                    );

                    _ = _taskScheduler.QueueAsync(
                        async ct =>
                        {
                            if (semaphore != null)
                            {
                                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                            }

                            try
                            {
                                await _taskHandler.ExecuteTaskAsync(function, false, ct);
                            }
                            finally
                            {
                                semaphore?.Release();
                            }
                        },
                        function.CachedPriority,
                        stoppingToken
                    );
                }

                _executionContext.SetFunctions(null);
            }

            var (timeRemaining, functions) = await _internalJobsManager.GetNextTickers(cancellationToken);

            _executionContext.SetFunctions(functions);

            TimeSpan sleepDuration;
            if (timeRemaining == Timeout.InfiniteTimeSpan || timeRemaining > TimeSpan.FromDays(1))
            {
                sleepDuration = TimeSpan.FromDays(1);
                _executionContext.SetNextPlannedOccurrence(null);
                _executionContext.SetFunctions(null);
            }
            else
            {
                sleepDuration = timeRemaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : timeRemaining;
                _executionContext.SetNextPlannedOccurrence(DateTime.UtcNow.Add(sleepDuration));
            }

            var notify = _executionContext.NotifyCoreAction;
            if (notify != null)
            {
                notify(_executionContext.GetNextPlannedOccurrence(), CoreNotifyActionType.NotifyNextOccurence);
            }

            await Task.Delay(sleepDuration, cancellationToken);
        }
    }

    private async Task _ReleaseAllResourcesAsync(Exception ex)
    {
        if (ex != null && _executionContext.NotifyCoreAction != null)
        {
            _executionContext.NotifyCoreAction(ex.ToString(), CoreNotifyActionType.NotifyHostExceptionMessage);
        }

        await _internalJobsManager.ReleaseAcquiredResources([], CancellationToken.None);
    }

    public void RestartIfNeeded(DateTime? dateTime)
    {
        if (!dateTime.HasValue)
        {
            return;
        }

        var now = DateTime.UtcNow;
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
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _restartThrottle.Dispose();
        _schedulerLoopCancellationTokenSource?.Dispose();
        base.Dispose();
    }
}
