// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Jobs.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Jobs.JobsThreadPool;

/// <summary>
/// Elastic work-stealing task scheduler.
/// </summary>
internal sealed class JobsTaskScheduler : IAsyncDisposable
{
    private readonly int _maxConcurrency;
    private readonly TimeSpan _idleWorkerTimeout;
    private readonly int _maxCapacityPerWorker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobsTaskScheduler> _logger;
    private readonly Func<int, CancellationToken, Task>? _workerStartGate;
    private readonly SemaphoreSlim _longRunningSlots;

    // Worker-fault restart backoff: start at 100ms, double per consecutive fault up to a 30s ceiling, and stop
    // auto-restarting a slot after this many back-to-back faults so a deterministically-faulting slot cannot spin
    // (log -> restart -> log) forever while liveness stays green.
    private static readonly TimeSpan _WorkerFaultRestartDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _MaxWorkerFaultRestartDelay = TimeSpan.FromSeconds(30);
    private const int _MaxConsecutiveWorkerFaults = 5;

    // Worker queues for work stealing
    private readonly WorkerQueue[] _workerQueues;
    private readonly Lock _workerSlotsLock = new();
    private readonly Task?[] _workerTasks;

    // Per-slot consecutive-fault counter; slot-partitioned (one worker task per index at a time) but written via
    // Interlocked so the reset on the restarting task always observes the prior task's increment.
    private readonly int[] _workerFaultCounts;

    // Global state
    private volatile int _totalQueuedTasks;
    private volatile int _activeTasks;
    private volatile int _activeWorkers;
    private volatile int _queuedHighPriority;
    private volatile int _queuedNormalPriority;
    private volatile int _queuedLowPriority;
    private readonly int _maxPendingLongRunningAdmissions;
    private volatile int _outstandingLongRunningOperations;
    private volatile int _pendingLongRunningAdmissions;
    private volatile int _longRunningSlotsDisposed;
    private volatile bool _disposed;
    private volatile bool _isFrozen;
    private volatile int _nextQueueIndex;

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SoftSchedulerNotifyDebounce _notifyDebounce;

    public JobsTaskScheduler(
        int maxConcurrency,
        int? maxLongRunningConcurrency = null,
        TimeSpan? idleWorkerTimeout = null,
        SoftSchedulerNotifyDebounce? notifyDebounce = null,
        TimeProvider? timeProvider = null,
        ILogger<JobsTaskScheduler>? logger = null,
        Func<int, CancellationToken, Task>? workerStartGate = null
    )
    {
        _maxConcurrency = Argument.IsPositive(maxConcurrency);
        _idleWorkerTimeout = idleWorkerTimeout ?? TimeSpan.FromSeconds(60);
        _maxCapacityPerWorker = 1024; // Fixed optimal capacity
        _notifyDebounce = notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { });
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<JobsTaskScheduler>.Instance;
        _workerStartGate = workerStartGate;
        var longRunningSlotCount = Argument.IsPositive(maxLongRunningConcurrency ?? Math.Min(maxConcurrency, 4));
        _longRunningSlots = new SemaphoreSlim(longRunningSlotCount);
        _maxPendingLongRunningAdmissions = longRunningSlotCount * 2;

        // Initialize all worker queues upfront for simplicity
        _workerQueues = new WorkerQueue[maxConcurrency];
        _workerTasks = new Task?[maxConcurrency];
        _workerFaultCounts = new int[maxConcurrency];
        for (var i = 0; i < maxConcurrency; i++)
        {
            _workerQueues[i] = new WorkerQueue();
        }

        // Start at least one worker immediately to handle incoming tasks
        _TryStartWorker();
    }

    /// <summary>
    /// Queues work to be executed by the scheduler.
    /// High-priority work is dequeued before normal and low-priority work on the shared worker pool.
    /// <see cref="JobPriority.LongRunning"/> work returns as soon as the admission is registered: the bounded
    /// dedicated-thread permit is awaited on a detached lane so a saturated long-running pool never blocks the
    /// caller's dispatch loop. The detached backlog is capped at two parked admissions per slot; beyond the cap
    /// the admission is rejected outright. A rejected or dropped (cancellation/shutdown) admission never runs;
    /// the claimed job is recovered by the fallback reclaim sweep when its pickup lease lapses.
    /// </summary>
    public Task QueueAsync(
        Func<CancellationToken, Task> work,
        JobPriority priority,
        CancellationToken cancellationToken = default
    )
    {
        return QueueAsync(work, priority, cancellationToken, cancellationToken);
    }

    internal async Task QueueAsync(
        Func<CancellationToken, Task> work,
        JobPriority priority,
        CancellationToken capacityCancellationToken,
        CancellationToken executionCancellationToken
    )
    {
        Argument.IsNotNull(work);

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isFrozen)
        {
            throw new InvalidOperationException("Scheduler is frozen");
        }

        capacityCancellationToken.ThrowIfCancellationRequested();

        // Handle long-running tasks specially
        if (priority == JobPriority.LongRunning)
        {
            // Bound the detached admission backlog at two parked waiters per slot. Under sustained saturation the
            // fallback sweep re-claims a still-parked job every time its pickup lease lapses and dispatches it
            // again, so an unbounded backlog would grow by one waiter per job per lease cycle. Beyond the cap the
            // admission is rejected outright — the job stays claimed until its lease lapses and the reclaim sweep
            // re-dispatches it, so nothing is lost.
            var pendingAdmissions = Interlocked.Increment(ref _pendingLongRunningAdmissions);
            if (pendingAdmissions > _maxPendingLongRunningAdmissions)
            {
                Interlocked.Decrement(ref _pendingLongRunningAdmissions);
                _logger.LongRunningAdmissionRejected(pendingAdmissions - 1, _maxPendingLongRunningAdmissions);
                return;
            }

            // Reserve the outstanding-operation count synchronously so a disposal that starts after this method
            // returns cannot dispose the slot semaphore before the detached admission below observes it.
            Interlocked.Increment(ref _outstandingLongRunningOperations);

            // Admission is decoupled from the caller: a saturated long-running pool must not head-of-line-block a
            // sequential dispatch batch that still has ordinary-priority work to queue. A dropped admission
            // (caller cancellation or shutdown while parked) never runs; the claimed job is recovered by the
            // fallback reclaim sweep once its pickup lease lapses.
            _ = _AdmitAndStartLongRunningAsync(work, capacityCancellationToken, executionCancellationToken);

            return;
        }

        // Round-robin distribution across worker queues
        var queueIndex = _GetNextQueueIndex();
        var targetQueue = _workerQueues[queueIndex];

        // Wait for capacity if needed
        await _WaitForCapacityAsync(targetQueue, capacityCancellationToken).ConfigureAwait(false);
        capacityCancellationToken.ThrowIfCancellationRequested();

        // Publish counters before the item. A worker may briefly observe a positive count before the queue item is
        // visible, but it can never dequeue an item and decrement counters that have not been incremented yet.
        var workItem = new WorkItem(work, executionCancellationToken);
        _IncrementQueuedPriority(priority);
        var newTotal = Interlocked.Increment(ref _totalQueuedTasks);
        targetQueue.Enqueue(workItem, priority);

        // Ensure we have workers to process the work
        // Check both queue count and total to avoid race conditions
        if (newTotal > 0 || !targetQueue.IsEmpty)
        {
            _EnsureWorkerAvailable();
        }
    }

    private int _GetNextQueueIndex()
    {
        // Simple round-robin without complex CAS loop. Mask the sign bit rather than Math.Abs: when the counter
        // wraps to int.MinValue after 2^31 enqueues, Math.Abs(int.MinValue) throws OverflowException (it cannot
        // represent -int.MinValue). Masking keeps the round-robin distribution and never throws.
        var index = Interlocked.Increment(ref _nextQueueIndex);
        return (index & int.MaxValue) % _maxConcurrency;
    }

    private async ValueTask _WaitForCapacityAsync(WorkerQueue queue, CancellationToken cancellationToken)
    {
        var waitCount = 0;
        while (queue.Count >= _maxCapacityPerWorker && !cancellationToken.IsCancellationRequested)
        {
            if (++waitCount > 100) // After 1 second, check if we're stuck
            {
                // Force start a worker if we're waiting too long
                _EnsureWorkerAvailable();
                waitCount = 0;
            }
            await _timeProvider.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }
    }

    private void _EnsureWorkerAvailable()
    {
        // Always try to maintain at least one worker
        if (_activeWorkers == 0)
        {
            _TryStartWorker();
            return;
        }

        // If there is queued work and we still have capacity, start another worker
        var totalQueued = _totalQueuedTasks;
        var activeWorkers = _activeWorkers;

        if (totalQueued > 0 && activeWorkers < _maxConcurrency)
        {
            _TryStartWorker();
        }
    }

    private void _TryStartWorker()
    {
        int activeWorkers;

        lock (_workerSlotsLock)
        {
            if (_shutdownCts.IsCancellationRequested || _disposed || _activeWorkers >= _maxConcurrency)
            {
                return;
            }

            var workerId = -1;
            for (var i = 0; i < _workerTasks.Length; i++)
            {
                if (_workerTasks[i] is not null)
                {
                    continue;
                }

                workerId = i;
                break;
            }

            if (workerId < 0)
            {
                return;
            }

            activeWorkers = Interlocked.Increment(ref _activeWorkers);
            _workerTasks[workerId] = Task.Run(() => _WorkerLoopAsync(workerId));
        }

        _notifyDebounce.NotifySafely(activeWorkers);
    }

    private async Task _WorkerLoopAsync(int workerId)
    {
        var permanentlyDegraded = false;

        try
        {
            if (_workerStartGate is not null)
            {
                await _workerStartGate(workerId, _shutdownCts.Token).ConfigureAwait(false);
            }

            await _WorkerLoopCoreAsync(workerId).ConfigureAwait(false);

            // A clean exit (idle-timeout retirement) clears the consecutive-fault streak so a recovered slot gets a
            // full backoff budget next time it faults.
            Interlocked.Exchange(ref _workerFaultCounts[workerId], 0);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested || _disposed)
        {
            // Shutdown cancelled an in-flight await inside the loop (e.g. the idle backoff delay).
            Interlocked.Exchange(ref _workerFaultCounts[workerId], 0);
        }
        // ERP022/RCS1075: A scheduler fault must retire this slot without becoming unobserved.
#pragma warning disable ERP022, RCS1075
        catch (Exception ex)
        {
            var consecutiveFaults = Interlocked.Increment(ref _workerFaultCounts[workerId]);
            _logger.WorkerLoopFaulted(ex, workerId);

            if (consecutiveFaults >= _MaxConsecutiveWorkerFaults)
            {
                // Stop auto-restarting a slot that keeps faulting back-to-back: a deterministic scheduler-internal
                // fault would otherwise spin (log -> backoff -> restart) forever while liveness stays green. New work
                // arriving later can still revive the slot through _EnsureWorkerAvailable.
                permanentlyDegraded = true;
                _logger.WorkerSlotPermanentlyDegraded(workerId, consecutiveFaults);
            }
            else
            {
                try
                {
                    await Task.Delay(_GetWorkerFaultRestartDelay(consecutiveFaults), _shutdownCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested || _disposed)
                {
                    // Shutdown cancels the restart backoff; the finally block retires the slot without restarting.
                }
            }
        }
#pragma warning restore ERP022, RCS1075
        finally
        {
            int activeWorkers;
            bool restartWorker;

            lock (_workerSlotsLock)
            {
                _workerTasks[workerId] = null;
                activeWorkers = Interlocked.Decrement(ref _activeWorkers);
                restartWorker =
                    !permanentlyDegraded
                    && _totalQueuedTasks > 0
                    && !_shutdownCts.IsCancellationRequested
                    && !_disposed;
            }

            _notifyDebounce.NotifySafely(activeWorkers);

            if (restartWorker)
            {
                _EnsureWorkerAvailable();
            }
        }
    }

    private static TimeSpan _GetWorkerFaultRestartDelay(int consecutiveFaults)
    {
        // Exponential backoff from the base delay, doubling per consecutive fault, capped at the ceiling.
        // consecutiveFaults is >= 1 here, so the first fault waits exactly the base delay.
        var exponent = Math.Min(consecutiveFaults - 1, 30);
        var delayMs = _WorkerFaultRestartDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var cappedMs = Math.Min(delayMs, _MaxWorkerFaultRestartDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(cappedMs);
    }

    private async Task _WorkerLoopCoreAsync(int workerId)
    {
        var lastWorkTime = _timeProvider.GetUtcNow().UtcDateTime;
        var localQueue = _workerQueues[workerId];
        var consecutiveStealFailures = 0;

        while (!_disposed && !_shutdownCts.Token.IsCancellationRequested)
        {
            var foundWork = false;

            // Check each priority lane across the pool before falling through to a lower lane. A local low-priority
            // item must not jump ahead of high-priority work queued on another worker.
            if (_TryDequeueWork(workerId, localQueue, out var workItem))
            {
                foundWork = true;
                consecutiveStealFailures = 0;
            }
            else
            {
                consecutiveStealFailures++;
            }

            if (foundWork)
            {
                lastWorkTime = _timeProvider.GetUtcNow().UtcDateTime;
                await _ExecuteWorkAsync(workItem).ConfigureAwait(false);
            }
            else
            {
                // No work found - check if we should exit
                if (_timeProvider.GetUtcNow().UtcDateTime - lastWorkTime > _idleWorkerTimeout)
                {
                    // Check ALL queues for any remaining work before exiting
                    var anyWorkRemaining = false;
                    for (var i = 0; i < _maxConcurrency; i++)
                    {
                        if (!_workerQueues[i].IsEmpty)
                        {
                            anyWorkRemaining = true;
                            break;
                        }
                    }

                    // Only exit if there's really no work and we have minimum workers
                    if (!anyWorkRemaining && _totalQueuedTasks == 0 && _activeWorkers > 1)
                    {
                        break; // Exit this worker
                    }

                    // Reset timer if we need to stay
                    lastWorkTime = _timeProvider.GetUtcNow().UtcDateTime;
                }

                // Brief sleep to avoid spinning
                if (consecutiveStealFailures > 3)
                {
                    await _timeProvider
                        .Delay(
                            TimeSpan.FromMilliseconds(Math.Min(consecutiveStealFailures * 2, 50)),
                            _shutdownCts.Token
                        )
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
    }

    private bool _TryDequeueWork(int workerId, WorkerQueue localQueue, out WorkItem workItem)
    {
        foreach (var priority in WorkerQueue.OrderedPriorities)
        {
            if (_GetQueuedPriorityCount(priority) == 0)
            {
                continue;
            }

            if (localQueue.TryDequeue(priority, out workItem))
            {
                _DecrementQueuedPriority(priority);
                return true;
            }

            var startIndex = (workerId + 1) % _maxConcurrency;
            for (var i = 0; i < _maxConcurrency - 1; i++)
            {
                var victimIndex = (startIndex + i) % _maxConcurrency;
                if (victimIndex == workerId)
                {
                    continue;
                }

                if (_workerQueues[victimIndex].TryDequeue(priority, out workItem))
                {
                    _DecrementQueuedPriority(priority);
                    return true;
                }
            }
        }

        workItem = default;
        return false;
    }

    private async Task _ExecuteWorkAsync(WorkItem workItem)
    {
        // Move the item from queued to active without exposing a false-idle gap to drains.
        Interlocked.Increment(ref _activeTasks);
        Interlocked.Decrement(ref _totalQueuedTasks);

        try
        {
            // Check cancellation before executing
            if (!workItem.UserToken.IsCancellationRequested && !_shutdownCts.Token.IsCancellationRequested)
            {
                var task = workItem.Work(workItem.UserToken);

                if (task == null)
                {
                    return;
                }

                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected - task was cancelled
        }
        // ERP022/RCS1075: Worker thread must continue running even if tasks fail.
#pragma warning disable ERP022, RCS1075
        catch (Exception)
        {
            // Log error if needed, but don't crash the worker
            // Errors are swallowed to prevent worker thread crashes
        }
#pragma warning restore ERP022, RCS1075
        finally
        {
            Interlocked.Decrement(ref _activeTasks);
        }
    }

    private async Task _AdmitAndStartLongRunningAsync(
        Func<CancellationToken, Task> work,
        CancellationToken capacityCancellationToken,
        CancellationToken executionCancellationToken
    )
    {
        var admitted = false;

        // ERP022: a detached admission has no caller to observe a failure; cancellation, shutdown, and dispose
        // races all resolve to dropping the admission so the reclaim sweep can recover the job.
#pragma warning disable ERP022
        try
        {
            using var admissionCts = CancellationTokenSource.CreateLinkedTokenSource(
                capacityCancellationToken,
                _shutdownCts.Token
            );
            await _longRunningSlots.WaitAsync(admissionCts.Token).ConfigureAwait(false);
            admitted = true;
        }
        catch
        {
            Interlocked.Decrement(ref _outstandingLongRunningOperations);
            _logger.LongRunningAdmissionDropped(_pendingLongRunningAdmissions, _maxPendingLongRunningAdmissions);
            _TryDisposeLongRunningSlots();
        }
        finally
        {
            // The waiter has resolved either way; free its backlog slot (running work is bounded by the slot
            // semaphore, not the admission backlog).
            Interlocked.Decrement(ref _pendingLongRunningAdmissions);
        }
#pragma warning restore ERP022

        if (!admitted)
        {
            return;
        }

        // Bypass the shared pool only after reserving a bounded dedicated-thread slot.
        Interlocked.Increment(ref _activeTasks);

        try
        {
            _ = Task
                .Factory.StartNew(
                    () => _ExecuteLongRunningWorkAsync(work, executionCancellationToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
                .Unwrap();
        }
        // ERP022: same as the admission wait above — no caller can observe a detached start failure.
#pragma warning disable ERP022
        catch
        {
            Interlocked.Decrement(ref _activeTasks);
            _longRunningSlots.Release();
            Interlocked.Decrement(ref _outstandingLongRunningOperations);
            _TryDisposeLongRunningSlots();
        }
#pragma warning restore ERP022
    }

    private async Task _ExecuteLongRunningWorkAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!cancellationToken.IsCancellationRequested && !_shutdownCts.Token.IsCancellationRequested)
            {
                await work(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected - task was cancelled
        }
        // ERP022/RCS1075: Scheduler must continue running even if task execution throws.
#pragma warning disable ERP022, RCS1075
        catch (Exception)
        {
            // Errors are swallowed to prevent worker thread crashes.
        }
#pragma warning restore ERP022, RCS1075
        finally
        {
            _longRunningSlots.Release();
            Interlocked.Decrement(ref _outstandingLongRunningOperations);
            Interlocked.Decrement(ref _activeTasks);
            _TryDisposeLongRunningSlots();
        }
    }

    /// <summary>
    /// Freezes the scheduler - prevents new tasks from being queued.
    /// </summary>
    public void Freeze()
    {
        _isFrozen = true;
    }

    /// <summary>
    /// Resumes the scheduler - allows new tasks to be queued again.
    /// </summary>
    public void Resume()
    {
        _isFrozen = false;
    }

    /// <summary>
    /// Gets whether the scheduler is currently frozen.
    /// </summary>
    public bool IsFrozen => _isFrozen;

    /// <summary>
    /// Gets the current number of active logical workers.
    /// </summary>
    public int ActiveWorkers => _activeWorkers;

    /// <summary>
    /// Gets the current total number of queued tasks.
    /// </summary>
    public int TotalQueuedTasks => _totalQueuedTasks;

    /// <summary>
    /// Gets the current number of active task executions.
    /// </summary>
    public int ActiveTasks => _activeTasks;

    /// <summary>
    /// Gets whether the scheduler has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Gets diagnostic information about the scheduler state.
    /// </summary>
    public string GetDiagnostics()
    {
        var builder = new StringBuilder();
        builder.Append("=== Jobs Work-Stealing Scheduler ===\n");
        builder.Append(
            CultureInfo.InvariantCulture,
            $"Status: {(_isFrozen ? "FROZEN" : (_disposed ? "DISPOSED" : "ACTIVE"))}\n"
        );
        builder.Append(CultureInfo.InvariantCulture, $"Workers: {_activeWorkers}/{_maxConcurrency}\n");
        builder.Append(CultureInfo.InvariantCulture, $"Active Tasks: {_activeTasks}\n");
        builder.Append(CultureInfo.InvariantCulture, $"Total Queued (counter): {_totalQueuedTasks}\n\n");
        builder.Append("Queue Distribution:\n");

        var totalInQueues = 0;
        for (var i = 0; i < _maxConcurrency; i++)
        {
            var count = _workerQueues[i].Count;
            totalInQueues += count;
            if (count > 0)
            {
                builder.Append(CultureInfo.InvariantCulture, $"  Queue[{i}]: {count} tasks\n");
            }
        }

        if (totalInQueues == 0)
        {
            builder.Append("  All queues empty\n");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $"  Total in queues: {totalInQueues}\n");
        }

        // Discrepancy check
        if (totalInQueues != _totalQueuedTasks)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $"\n⚠️ DISCREPANCY: Counter shows {_totalQueuedTasks} but queues have {totalInQueues} tasks!\n"
            );
        }

        return builder.ToString();
    }

    /// <summary>
    /// Waits for all currently running tasks to complete.
    /// </summary>
    public async Task<bool> WaitForRunningTasksAsync(TimeSpan? timeout = null)
    {
        var deadline = timeout.HasValue ? _timeProvider.GetUtcNow().UtcDateTime.Add(timeout.Value) : DateTime.MaxValue;

        while (_totalQueuedTasks > 0 || _activeTasks > 0)
        {
            if (_timeProvider.GetUtcNow().UtcDateTime > deadline)
            {
                return false; // Timeout
            }

            await _timeProvider.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isFrozen = true; // Prevent new tasks
        await _shutdownCts.CancelAsync().ConfigureAwait(false);
        var shutdownDeadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(5);

        Task[] workerTasks;
        lock (_workerSlotsLock)
        {
            workerTasks = new Task[_workerTasks.Length];
            var count = 0;

            foreach (var workerTask in _workerTasks)
            {
                if (workerTask is not null)
                {
                    workerTasks[count++] = workerTask;
                }
            }

            if (count != workerTasks.Length)
            {
                Array.Resize(ref workerTasks, count);
            }
        }

        try
        {
            var remaining = shutdownDeadline - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.WhenAll(workerTasks)
                    .WaitAsync(remaining, _timeProvider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            // Preserve bounded shutdown when user work ignores cancellation.
        }

        while (_activeTasks > 0 && _timeProvider.GetUtcNow() < shutdownDeadline)
        {
            await _timeProvider.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None).ConfigureAwait(false);
        }

        _notifyDebounce.Dispose();
        _TryDisposeLongRunningSlots();
        _shutdownCts.Dispose();
    }

    private void _TryDisposeLongRunningSlots()
    {
        if (!_disposed || _outstandingLongRunningOperations != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _longRunningSlotsDisposed, 1) == 0)
        {
            _longRunningSlots.Dispose();
        }
    }

    private int _GetQueuedPriorityCount(JobPriority priority)
    {
        return priority switch
        {
            JobPriority.High => _queuedHighPriority,
            JobPriority.Low => _queuedLowPriority,
            _ => _queuedNormalPriority,
        };
    }

    private void _IncrementQueuedPriority(JobPriority priority)
    {
        if (priority == JobPriority.High)
        {
            Interlocked.Increment(ref _queuedHighPriority);
        }
        else if (priority == JobPriority.Low)
        {
            Interlocked.Increment(ref _queuedLowPriority);
        }
        else
        {
            Interlocked.Increment(ref _queuedNormalPriority);
        }
    }

    private void _DecrementQueuedPriority(JobPriority priority)
    {
        if (priority == JobPriority.High)
        {
            Interlocked.Decrement(ref _queuedHighPriority);
        }
        else if (priority == JobPriority.Low)
        {
            Interlocked.Decrement(ref _queuedLowPriority);
        }
        else
        {
            Interlocked.Decrement(ref _queuedNormalPriority);
        }
    }

    private sealed class WorkerQueue
    {
        public static readonly JobPriority[] OrderedPriorities =
        [
            JobPriority.High,
            JobPriority.Normal,
            JobPriority.Low,
        ];

        private readonly ConcurrentQueue<WorkItem> _high = new();
        private readonly ConcurrentQueue<WorkItem> _normal = new();
        private readonly ConcurrentQueue<WorkItem> _low = new();
        private int _count;

        public int Count => _count;

        public bool IsEmpty => _count == 0;

        public void Enqueue(WorkItem workItem, JobPriority priority)
        {
            Interlocked.Increment(ref _count);
            _GetQueue(priority).Enqueue(workItem);
        }

        public bool TryDequeue(JobPriority priority, out WorkItem workItem)
        {
            if (!_GetQueue(priority).TryDequeue(out workItem))
            {
                return false;
            }

            Interlocked.Decrement(ref _count);
            return true;
        }

        private ConcurrentQueue<WorkItem> _GetQueue(JobPriority priority)
        {
            return priority switch
            {
                JobPriority.High => _high,
                JobPriority.Low => _low,
                _ => _normal,
            };
        }
    }
}

internal static partial class JobsTaskSchedulerLog
{
    [LoggerMessage(
        EventId = 3300,
        EventName = "JobsWorkerLoopFaulted",
        Level = LogLevel.Error,
        Message = "Jobs worker slot {WorkerId} faulted outside user work; restart is rate-limited."
    )]
    public static partial void WorkerLoopFaulted(this ILogger logger, Exception exception, int workerId);

    [LoggerMessage(
        EventId = 3301,
        EventName = "JobsWorkerSlotPermanentlyDegraded",
        Level = LogLevel.Critical,
        Message = "Jobs worker slot {WorkerId} degraded after {ConsecutiveFaults} consecutive faults; auto-restart stopped until new work arrives."
    )]
    public static partial void WorkerSlotPermanentlyDegraded(this ILogger logger, int workerId, int consecutiveFaults);

    [LoggerMessage(
        EventId = 3302,
        EventName = "JobsLongRunningAdmissionRejected",
        Level = LogLevel.Debug,
        Message = "Long-running admission rejected at backlog cap ({PendingAdmissions}/{MaxPendingAdmissions} parked); the reclaim sweep re-dispatches the still-claimed job when its pickup lease lapses."
    )]
    public static partial void LongRunningAdmissionRejected(
        this ILogger logger,
        int pendingAdmissions,
        int maxPendingAdmissions
    );

    [LoggerMessage(
        EventId = 3303,
        EventName = "JobsLongRunningAdmissionDropped",
        Level = LogLevel.Debug,
        Message = "Parked long-running admission dropped by cancellation or shutdown ({PendingAdmissions}/{MaxPendingAdmissions} parked); the reclaim sweep recovers the claimed job when its pickup lease lapses."
    )]
    public static partial void LongRunningAdmissionDropped(
        this ILogger logger,
        int pendingAdmissions,
        int maxPendingAdmissions
    );
}
