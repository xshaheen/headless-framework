namespace Headless.Jobs.JobsThreadPool;

/// <summary>
/// Custom SynchronizationContext that keeps async continuations on Jobs worker threads.
/// Ensures true concurrency control by preventing execution from switching to ThreadPool.
/// Uses flexible thread assignment for optimal performance and load balancing.
/// </summary>
internal sealed class JobsSynchronizationContext(JobsTaskScheduler scheduler) : SynchronizationContext
{
    /// <summary>
    /// Posts async continuations to Jobs workers with circular dependency protection.
    /// Uses direct execution if called from within a Jobs worker to prevent deadlocks.
    /// </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        // Check if we're already on a Jobs worker thread using ThreadStatic flag
        if (JobsTaskScheduler.IsJobsWorkerThread)
        {
            // We're on a Jobs worker - execute directly to avoid circular queueing
            try
            {
                d(state);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                /* swallow continuation exceptions */
            }
#pragma warning restore ERP022
        }
        else
        {
            // We're not on a Jobs worker - safe to queue the continuation
            scheduler.PostContinuation(d, state);
        }
    }

    /// <summary>
    /// Sends synchronous operations (not typically used in async scenarios).
    /// </summary>
    public override void Send(SendOrPostCallback d, object? state)
    {
        // For synchronous operations, execute directly
        d(state);
    }
}
