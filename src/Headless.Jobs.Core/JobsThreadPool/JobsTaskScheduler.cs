// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Jobs.Enums;

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

    // Worker queues for work stealing
    private readonly WorkerQueue[] _workerQueues;
    private readonly Lock _workerSlotsLock = new();
    private readonly Task?[] _workerTasks;

    // Global state
    private volatile int _totalQueuedTasks;
    private volatile int _activeTasks;
    private volatile int _activeWorkers;
    private volatile int _queuedHighPriority;
    private volatile int _queuedNormalPriority;
    private volatile int _queuedLowPriority;
    private volatile bool _disposed;
    private volatile bool _isFrozen;
    private volatile int _nextQueueIndex;

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SoftSchedulerNotifyDebounce _notifyDebounce;

    public JobsTaskScheduler(
        int maxConcurrency,
        TimeSpan? idleWorkerTimeout = null,
        SoftSchedulerNotifyDebounce? notifyDebounce = null,
        TimeProvider? timeProvider = null
    )
    {
        _maxConcurrency = Argument.IsPositive(maxConcurrency);
        _idleWorkerTimeout = idleWorkerTimeout ?? TimeSpan.FromSeconds(60);
        _maxCapacityPerWorker = 1024; // Fixed optimal capacity
        _notifyDebounce = notifyDebounce ?? new SoftSchedulerNotifyDebounce(_ => { });
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Initialize all worker queues upfront for simplicity
        _workerQueues = new WorkerQueue[maxConcurrency];
        _workerTasks = new Task?[maxConcurrency];
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
            // Bypass pool for long-running tasks
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
            catch
            {
                Interlocked.Decrement(ref _activeTasks);
                throw;
            }

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
        // Simple round-robin without complex CAS loop
        var index = Interlocked.Increment(ref _nextQueueIndex);
        return Math.Abs(index) % _maxConcurrency;
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
        try
        {
            await _WorkerLoopCoreAsync(workerId).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested || _disposed)
        {
            // Shutdown cancelled an in-flight await inside the loop (e.g. the idle backoff delay).
        }
        // ERP022/RCS1075: A scheduler fault must retire this slot without becoming unobserved.
#pragma warning disable ERP022, RCS1075
        catch (Exception)
        {
            // User work is isolated in _ExecuteWorkAsync. This guard observes infrastructure faults so a slot can
            // retire and queued work can be picked up by another worker.
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
                restartWorker = _totalQueuedTasks > 0 && !_shutdownCts.IsCancellationRequested && !_disposed;
            }

            _notifyDebounce.NotifySafely(activeWorkers);

            if (restartWorker)
            {
                _EnsureWorkerAvailable();
            }
        }
    }

    private async Task _WorkerLoopCoreAsync(int workerId)
    {
        var lastWorkTime = _timeProvider.GetUtcNow().UtcDateTime;
        var localQueue = _workerQueues[workerId];
        var consecutiveStealFailures = 0;

        while (!_shutdownCts.Token.IsCancellationRequested && !_disposed)
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
            Interlocked.Decrement(ref _activeTasks);
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
        _shutdownCts.Dispose();
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
