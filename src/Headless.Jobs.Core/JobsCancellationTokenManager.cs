// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>
/// Process-wide registry of per-job <c>CancellationTokenSource</c> instances that allows external
/// callers to cancel a running job by its identifier without direct access to the executing worker.
/// </summary>
/// <remarks>
/// This class is used internally by the scheduler and by the dashboard cancel endpoint. Direct use
/// from application code is rarely needed; prefer cooperative cancellation via
/// <c>JobFunctionContext.RequestCancellation</c> inside the job function itself.
/// </remarks>
public static class JobsCancellationTokenManager
{
    private static readonly ConcurrentDictionary<Guid, JobsCancellationTokenDetails> _TickerCancellationTokens = new();
    private static readonly ConcurrentDictionary<Guid, ConcurrentHashSet<Guid>> _ParentIdIndex = new();

    internal static void AddTickerCancellationToken(
        CancellationTokenSource cancellationSource,
        JobExecutionState context,
        bool isDue
    )
    {
        var details = new JobsCancellationTokenDetails
        {
            FunctionName = context.FunctionName,
            Type = context.Type,
            CancellationSource = cancellationSource,
            IsDue = isDue,
            ParentId = context.ParentId ?? Guid.Empty,
        };

        _TickerCancellationTokens.TryAdd(context.JobId, details);

        // Add to parent index for fast lookup if parentId exists
        if (context.ParentId.HasValue && context.ParentId.Value != Guid.Empty)
        {
            _ParentIdIndex.AddOrUpdate(
                context.ParentId.Value,
                static (_, jobId) =>
                {
                    var set = new ConcurrentHashSet<Guid>();
                    set.Add(jobId);
                    return set;
                },
                static (_, existing, jobId) =>
                {
                    existing.Add(jobId);
                    return existing;
                },
                context.JobId
            );
        }
    }

    internal static bool RemoveTickerCancellationToken(Guid jobId)
    {
        var removed = _TickerCancellationTokens.TryRemove(jobId, out var details);

        if (removed && details != null)
        {
            // CRITICAL: Dispose CancellationTokenSource to prevent memory leak
            try
            {
                details.CancellationSource?.Dispose();
            }
#pragma warning disable ERP022 // Disposal errors during cleanup should not crash the scheduler.
            catch
            {
                // Ignore disposal errors
            }
#pragma warning restore ERP022

            // Remove from parent index if it exists
            if (details.ParentId != Guid.Empty)
            {
                if (_ParentIdIndex.TryGetValue(details.ParentId, out var set))
                {
                    set.Remove(jobId);
                    // Clean up empty sets
                    if (set.IsEmpty)
                    {
                        if (_ParentIdIndex.TryRemove(details.ParentId, out var removedSet))
                        {
                            // Dispose the ConcurrentHashSet to free ReaderWriterLockSlim
                            removedSet?.Dispose();
                        }
                    }
                }
            }
        }

        return removed;
    }

    internal static void CleanUpTickerCancellationTokens()
    {
        // CRITICAL: Must dispose all CancellationTokenSources before clearing to prevent memory leaks
        foreach (var kvp in _TickerCancellationTokens)
        {
            try
            {
                kvp.Value.CancellationSource?.Dispose();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // Ignore disposal errors during cleanup
            }
#pragma warning restore ERP022
        }

        _TickerCancellationTokens.Clear();

        // Dispose all ConcurrentHashSet instances
        foreach (var kvp in _ParentIdIndex)
        {
            try
            {
                kvp.Value?.Dispose();
            }
            // ERP022: Disposal errors during cleanup should not crash the scheduler.
#pragma warning disable ERP022
            catch
            {
                // Ignore disposal errors during cleanup
            }
#pragma warning restore ERP022
        }

        _ParentIdIndex.Clear();
    }

    /// <summary>
    /// Cancels the running job with the specified identifier and removes its entry from the registry.
    /// </summary>
    /// <param name="jobId">The identifier of the job to cancel.</param>
    /// <returns>
    /// <see langword="true"/> when the job was found and its cancellation was requested;
    /// <see langword="false"/> when no running job with that identifier exists.
    /// </returns>
    public static bool RequestTickerCancellationById(Guid jobId)
    {
        if (!_TickerCancellationTokens.TryRemove(jobId, out var tickerCancellationToken))
        {
            return false;
        }

        try
        {
            tickerCancellationToken.CancellationSource.Cancel();
        }
        finally
        {
            // CRITICAL: Must dispose CancellationTokenSource to prevent memory leak
            tickerCancellationToken.CancellationSource.Dispose();
        }

        // Remove from parent index if it exists
        if (tickerCancellationToken.ParentId != Guid.Empty)
        {
            if (_ParentIdIndex.TryGetValue(tickerCancellationToken.ParentId, out var set))
            {
                set.Remove(jobId);
                if (set.IsEmpty)
                {
                    if (_ParentIdIndex.TryRemove(tickerCancellationToken.ParentId, out var removedSet))
                    {
                        // Dispose the ConcurrentHashSet to free ReaderWriterLockSlim
                        removedSet?.Dispose();
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Fast O(1) lookup to check if any jobs are running for a given parent ID.
    /// This replaces the expensive LINQ Any() operation with a direct dictionary lookup.
    /// </summary>
    /// <param name="parentId">The parent ID to check</param>
    /// <returns>True if any jobs are running for this parent ID</returns>
    public static bool IsParentRunning(Guid parentId)
    {
        return _ParentIdIndex.ContainsKey(parentId);
    }

    /// <summary>
    /// Checks if any OTHER jobs (excluding the current one) are running for a given parent ID.
    /// Used to prevent false positives when checking if a sibling occurrence is already running.
    /// </summary>
    /// <param name="parentId">The parent ID to check</param>
    /// <param name="excludeJobId">The job ID to exclude from the check (usually the current job)</param>
    /// <returns>True if any other jobs are running for this parent ID</returns>
    public static bool IsParentRunningExcludingSelf(Guid parentId, Guid excludeJobId)
    {
        if (!_ParentIdIndex.TryGetValue(parentId, out var tickerSet))
        {
            return false;
        }

        return tickerSet.HasOtherItemsBesides(excludeJobId);
    }
}

/// <summary>
/// Carries the cancellation token source and scheduling metadata for a running job entry in
/// <c>JobsCancellationTokenManager</c>.
/// </summary>
internal sealed class JobsCancellationTokenDetails
{
    /// <summary>The registered function name of the running job.</summary>
    public required string FunctionName { get; set; }

    /// <summary>Whether the running entry is a time job or a cron occurrence.</summary>
    public JobType Type { get; set; }

    /// <summary><see langword="true"/> when the job was dispatched from the stale-job backlog.</summary>
    public bool IsDue { get; set; }

    /// <summary>The cancellation token source that can be used to cancel this job's execution.</summary>
    public required CancellationTokenSource CancellationSource { get; set; }

    /// <summary>
    /// Parent job identifier for chained time jobs, or <see cref="Guid.Empty"/> for root jobs and
    /// cron occurrences.
    /// </summary>
    public Guid ParentId { get; set; }
}

/// <summary>
/// Thread-safe HashSet implementation for concurrent operations
/// </summary>
internal sealed class ConcurrentHashSet<T> : IDisposable
{
    private readonly HashSet<T> _set = [];
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    public bool Add(T item)
    {
        try
        {
            _lock.EnterWriteLock();
            return _set.Add(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Remove(T item)
    {
        try
        {
            _lock.EnterWriteLock();
            return _set.Remove(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool IsEmpty
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _set.Count == 0;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Checks if there are any items in the set other than the specified excluded item.
    /// </summary>
    /// <param name="excludeItem">The item to exclude from the check</param>
    /// <returns>True if there are other items besides the excluded one</returns>
    public bool HasOtherItemsBesides(T excludeItem)
    {
        try
        {
            _lock.EnterReadLock();
            if (_set.Count == 0)
            {
                return false;
            }

            if (_set.Count == 1)
            {
                return !_set.Contains(excludeItem);
            }

            // Multiple items - at least one must be different from excludeItem
            return true;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _Dispose(disposing: true);
    }

    private void _Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _lock.Dispose();
        }

        _disposed = true;
    }
}
