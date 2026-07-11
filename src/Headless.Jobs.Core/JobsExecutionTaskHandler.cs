// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal sealed class JobsExecutionTaskHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IJobsInstrumentation _jobsInstrumentation;
    private readonly IInternalJobManager _internalJobsManager;
    private readonly ILogger<JobsExecutionTaskHandler> _logger;

    // #316 sliding lease: cadence at which a running job renews its own lease (≈ LeaseDuration/3 by default).
    private readonly TimeSpan _leaseRenewalInterval;

    // #461: how long a running job may tolerate unestablished coordination membership before the renewal loop treats
    // it as a lost lease — the lease window, after which the row is certainly being reclaimed elsewhere.
    private readonly TimeSpan _leaseDuration;
    private readonly JobsRetryOptions _retryOptions;
    private readonly JobsRetryPipeline _retryPipeline;

    public JobsExecutionTaskHandler(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        IJobsInstrumentation jobsInstrumentation,
        IInternalJobManager internalJobsManager,
        SchedulerOptionsBuilder schedulerOptions,
        ILogger<JobsExecutionTaskHandler> logger,
        JobsRetryOptions? retryOptions = null
    )
    {
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _jobsInstrumentation = jobsInstrumentation;
        _internalJobsManager = internalJobsManager;
        _logger = logger;
        _leaseRenewalInterval = schedulerOptions.ResolveLeaseRenewalInterval();
        _leaseDuration = schedulerOptions.LeaseDuration;
        _retryOptions = retryOptions ?? new JobsRetryOptions();
        _retryPipeline = new JobsRetryPipeline(_retryOptions, timeProvider, logger);
    }

    public Task ExecuteTaskAsync(
        JobExecutionState context,
        bool isDue,
        CancellationToken cancellationToken = default
    ) => _ExecuteTaskAsync(context, isDue, isChild: false, cancellationToken);

    private async Task _ExecuteTaskAsync(
        JobExecutionState context,
        bool isDue,
        bool isChild,
        CancellationToken cancellationToken
    )
    {
        if (context.Type == JobType.CronJobOccurrence)
        {
            await _RunContextFunctionAsync(context, isDue, cancellationToken, isChild).ConfigureAwait(false);
            return;
        }

        var childrenToRunAfter = new JobExecutionState[5];
        var tasksToRunNow = new Task[6];

        var childrenToRunAfterCount = 0;
        var tasksToRunNowCount = 0;

        var hasChildren = context.TimeJobChildren.Count > 0;

        // Add parent task
        tasksToRunNow[tasksToRunNowCount++] = _RunContextFunctionAsync(context, isDue, cancellationToken, isChild);

        if (hasChildren)
        {
            // Process children - separate InProgress from others
            foreach (var child in context.TimeJobChildren)
            {
                if (child.CachedDelegate != null)
                {
                    if (child.RunCondition == RunCondition.InProgress)
                    {
                        tasksToRunNow[tasksToRunNowCount++] = _SafeRecursiveExecution(child, isDue, cancellationToken);
                    }
                    else
                    {
                        childrenToRunAfter[childrenToRunAfterCount++] = child;
                    }
                }
            }
        }

        // Wait for concurrent tasks (parent + InProgress children)
        await Task.WhenAll(tasksToRunNow.AsSpan(0, tasksToRunNowCount).ToArray()).ConfigureAwait(false);

        // Process deferred children after parent completion
        if (childrenToRunAfterCount > 0)
        {
            var childrenToSkip = new List<JobExecutionState>(30); // Pre-sized for performance
            var childrenToRunAfterTask = new Task[childrenToRunAfterCount];

            var taskCount = 0;

            for (var i = 0; i < childrenToRunAfterCount; i++)
            {
                var child = childrenToRunAfter[i];

                if (child.CachedDelegate != null)
                {
                    if (_ShouldRunChild(child, context.Status))
                    {
                        childrenToRunAfterTask[taskCount++] = _SafeRecursiveExecution(child, isDue, cancellationToken);
                    }
                    else
                    {
                        _jobsInstrumentation.LogJobSkipped(
                            child.JobId,
                            child.FunctionName,
                            $"Condition {child.RunCondition} not met (Parent status: {context.Status})"
                        );
                        child.ParentId = context.JobId;
                        childrenToSkip.Add(child);

                        // Recursively gather all descendants to skip
                        _GatherDescendantsToSkip(child, childrenToSkip);
                    }
                }
            }

            // Bulk update skipped children
            if (childrenToSkip.Count > 0)
            {
                await _internalJobsManager
                    .UpdateSkipTimeJobsWithUnifiedContextAsync([.. childrenToSkip], cancellationToken)
                    .ConfigureAwait(false);
            }

            // Wait for deferred tasks
            if (taskCount > 0)
            {
                await Task.WhenAll(childrenToRunAfterTask.AsSpan(0, taskCount).ToArray()).ConfigureAwait(false);
            }
        }
    }

    private async Task _RunContextFunctionAsync(
        JobExecutionState context,
        bool isDue,
        CancellationToken cancellationToken,
        bool isChild = false
    )
    {
        // Start OpenTelemetry activity for the entire job execution
        using var jobActivity = _jobsInstrumentation.StartJobActivity(
            $"job.execute.{context.Type.ToString().ToLowerInvariant()}",
            context
        );

        // Add additional tags to the activity
        jobActivity?.SetTag("headless.job.is_due", isDue);
        jobActivity?.SetTag("headless.job.is_child", isChild);

        // Log job enqueued/started (using the available method)
        _jobsInstrumentation.LogJobEnqueued(
            context.Type.ToString(),
            context.FunctionName,
            context.JobId,
            "ExecutionTaskHandler"
        );

        context.SetProperty(x => x.Status, JobStatus.InProgress);

        if (isChild)
        {
            await _internalJobsManager.UpdateTickerAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (!await _VerifyLeaseBeforeExecutionAsync(context, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var stopWatch = new Stopwatch();
        // CA2000: ownership is registered into JobsCancellationTokenManager and the CTS is disposed on every exit
        // path below (after StopRenewalAsync stops the renewal loop that references it).
#pragma warning disable CA2000
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
#pragma warning restore CA2000

        // IMPORTANT: Register the job FIRST, before creating the SkipIfAlreadyRunningAction callback
        // This ensures the current occurrence is properly tracked when the callback checks for siblings
        JobsCancellationTokenManager.AddTickerCancellationToken(cancellationTokenSource, context, isDue);

        var jobFunctionContext = new JobFunctionContext
        {
            FunctionName = context.FunctionName,
            Id = context.JobId,
            Type = context.Type,
            IsDue = isDue,
            ScheduledFor = context.ExecutionTime,
            // The running job invokes this action during execution (before any exit path disposes the CTS — see the
            // CA2000 note above), so the captured cancellationTokenSource is alive whenever the closure runs.
            RequestCancelOperationAction = () => _ = cancellationTokenSource.CancelAsync(),
            CronOccurrenceOperations = new CronOccurrenceOperations
            {
                SkipIfAlreadyRunningAction = () =>
                {
                    if (context.Type == JobType.TimeJob)
                    {
                        return;
                    }

                    // Check for other running occurrences of the same parent (excluding self)
                    // Since we're already registered, we need to exclude ourselves from the check
                    var isRunning =
                        context.ParentId.HasValue
                        && JobsCancellationTokenManager.IsParentRunningExcludingSelf(
                            context.ParentId.Value,
                            context.JobId
                        );

                    if (isRunning)
                    {
                        throw new TerminateExecutionException("Another CronOccurrence is already running!");
                    }
                },
            },
        };

        // #316 sliding lease: renew this job's lease on a cadence for the whole execution (every retry attempt and
        // backoff wait). A renewal affecting 0 rows means the lease was lost (reclaimed / owner changed /
        // terminalized); the loop then cancels cancellationTokenSource, cancelling the running job (U1/U2/KTD3).
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = _RenewLeaseLoopAsync(context, cancellationTokenSource, renewalCts.Token);
        var renewalStopped = false;

        async Task stopRenewalAsync()
        {
            if (renewalStopped)
            {
                return;
            }

            renewalStopped = true;
            // StopRenewalAsync is a local function invoked on each exit path before `using var renewalCts` disposes
            // at method end, so renewalCts is never disposed when this runs.
            // ReSharper disable once AccessToDisposedClosure
            await renewalCts.CancelAsync().ConfigureAwait(false);
            // ERP022: renewal-loop teardown errors are non-fatal to job completion.
            // VSTHRD003: renewalTask is started locally (above) and intentionally awaited to bound the loop's lifetime.
#pragma warning disable ERP022, VSTHRD003
            try
            {
                await renewalTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
#pragma warning restore ERP022, VSTHRD003
        }

        Exception? lastException = null;
        var lastFailureRetryable = false;
        var success = false;

        try
        {
            await _retryPipeline
                .ExecuteAsync(
                    context,
                    async (retryCount, attemptToken) =>
                    {
                        jobFunctionContext.RetryCount = retryCount;
                        jobActivity?.SetTag("headless.job.current_attempt", retryCount + 1);
                        stopWatch.Start();
                        await using var scope = _serviceProvider.CreateAsyncScope();
                        jobFunctionContext.SetServiceScope(scope);
                        await context
                            .CachedDelegate(attemptToken, scope.ServiceProvider, jobFunctionContext)
                            .ConfigureAwait(false);
                        success = true;
                    },
                    async (retryCount, exception, retryToken) =>
                    {
                        context.SetProperty(x => x.RetryCount, retryCount);
                        var affected = await _internalJobsManager
                            .UpdateTickerAsync(context, retryToken)
                            .ConfigureAwait(false);
                        context.ResetUpdateProps();
                        if (affected == 0)
                        {
                            context.LeaseLost = true;
                            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
                            throw new OperationCanceledException(cancellationTokenSource.Token);
                        }

                        await _ObserveJobExceptionAsync(exception, context, retryToken).ConfigureAwait(false);
                    },
                    retryable => lastFailureRetryable = retryable,
                    cancellationTokenSource.Token
                )
                .ConfigureAwait(false);
        }
        // Only cancellation tied to this job (its own token / lease loss) is classified as Cancelled; a foreign
        // OperationCanceledException thrown by user code (e.g. a provider timeout) falls through to the generic
        // failure path below so it terminalizes as Failed instead of Cancelled (PR #643 fix, preserved across the
        // Polly pipeline rewrite).
        catch (OperationCanceledException ex)
            when (context.LeaseLost || cancellationTokenSource.IsCancellationRequested)
        {
            if (context.LeaseLost)
            {
                // #1/#463: lease loss leaves the row InProgress for recovery and prevents stale writes.
                _logger.LogJobLeaseLostCancellation(context.JobId, context.FunctionName);
                await stopRenewalAsync().ConfigureAwait(false);
                cancellationTokenSource.Dispose();
                JobsCancellationTokenManager.RemoveTickerCancellationToken(context.JobId);
                return;
            }

            context
                .SetProperty(x => x.Status, JobStatus.Cancelled)
                .SetProperty(x => x.ExecutedAt, _timeProvider.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
                .SetProperty(x => x.ExceptionDetails, _SerializeException(ex));

            // Add cancellation tags to activity
            jobActivity?.SetTag("headless.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("headless.job.cancellation.reason", "Task was cancelled");

            // Log job cancelled
            _jobsInstrumentation.LogJobCancelled(context.JobId, context.FunctionName, "Task was cancelled");

            if (_serviceProvider.GetService(typeof(IJobExceptionHandler)) is IJobExceptionHandler handler)
            {
                await handler
                    .HandleCanceledExceptionAsync(ex, context.JobId, context.Type, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Terminal-status write must persist even on graceful host-stop (cancellationToken already cancelled)
            // or lease-loss; the WhereOwnedBy completion fence already prevents clobbering a sweep's result.
            await _internalJobsManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);

            // Clean up and exit early on cancellation
            await stopRenewalAsync().ConfigureAwait(false);
            cancellationTokenSource?.Dispose();
            JobsCancellationTokenManager.RemoveTickerCancellationToken(context.JobId);
            return;
        }
        catch (TerminateExecutionException ex)
        {
            context
                .SetProperty(x => x.Status, ex.Status)
                .SetProperty(x => x.ExecutedAt, _timeProvider.GetUtcNow().UtcDateTime)
                .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds);

            if (ex.InnerException != null)
            {
                context.SetProperty(x => x.ExceptionDetails, ex.InnerException.Message);
                jobActivity?.SetTag("headless.job.skip.reason", ex.InnerException.Message);
            }
            else
            {
                context.SetProperty(x => x.ExceptionDetails, ex.Message);
                jobActivity?.SetTag("headless.job.skip.reason", ex.Message);
            }

            // Add skip tags to activity
            jobActivity?.SetTag("headless.job.final_status", context.Status.ToString());

            // Log job skipped
            _jobsInstrumentation.LogJobSkipped(context.JobId, context.FunctionName, ex.Message);

            // Terminal-status write must persist even on graceful host-stop (cancellationToken already cancelled)
            // or lease-loss; the WhereOwnedBy completion fence already prevents clobbering a sweep's result.
            await _internalJobsManager.UpdateTickerAsync(context, CancellationToken.None).ConfigureAwait(false);

            // Clean up and exit early on termination
            await stopRenewalAsync().ConfigureAwait(false);
            cancellationTokenSource.Dispose();
            JobsCancellationTokenManager.RemoveTickerCancellationToken(context.JobId);
            return;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }

        if (context.LeaseLost)
        {
            // User delegates are cooperative by contract but can ignore cancellation. The
            // ownership fence remains authoritative even when such a delegate returns or throws
            // after renewal detected lease loss: do not attempt terminal writes or callbacks.
            _logger.LogJobLeaseLostCancellation(context.JobId, context.FunctionName);
            await stopRenewalAsync().ConfigureAwait(false);
            cancellationTokenSource.Dispose();
            JobsCancellationTokenManager.RemoveTickerCancellationToken(context.JobId);
            return;
        }

        stopWatch.Stop();

        context
            .SetProperty(x => x.ElapsedTime, stopWatch.ElapsedMilliseconds)
            .SetProperty(x => x.ExecutedAt, _timeProvider.GetUtcNow().UtcDateTime);

        if (success)
        {
            context.SetProperty(x => x.Status, isDue ? JobStatus.DueDone : JobStatus.Succeeded);

            // Add success tags to activity
            jobActivity?.SetTag("headless.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("headless.job.final_retry.count", context.RetryCount);

            // Log job completed successfully
            _jobsInstrumentation.LogJobCompleted(
                context.JobId,
                context.FunctionName,
                stopWatch.ElapsedMilliseconds,
                success: true
            );

            // Terminal-status write must persist regardless of host-stop/lease-loss (completion fence guards it).
            var affected = await _internalJobsManager
                .UpdateTickerAsync(context, CancellationToken.None)
                .ConfigureAwait(false);
            if (affected == 0)
            {
                // #462: the success write matched 0 rows — the row was reclaimed/terminalized by a sweep (e.g. a
                // GC/thread-pool stall outlasted the lease before this write committed). The job actually succeeded,
                // but the durable record now shows the sweep's terminal status (Failed/Skipped). Log so operators can
                // reconcile instead of treating the recorded failure as real and manually re-triggering — the very
                // double-run that MarkFailed/Skip exist to prevent.
                _logger.LogJobCompletionFencedAfterSuccess(context.JobId, context.FunctionName);
            }
        }
        else if (lastException != null)
        {
            context
                .SetProperty(x => x.Status, JobStatus.Failed)
                .SetProperty(x => x.ExceptionDetails, _SerializeException(lastException));

            // Add failure tags to activity
            jobActivity?.SetTag("headless.job.final_status", context.Status.ToString());
            jobActivity?.SetTag("headless.job.final_retry.count", context.RetryCount);
            jobActivity?.SetTag("exception.type", lastException.GetType().Name);

            // Log job failed
            _jobsInstrumentation.LogJobFailed(context.JobId, context.FunctionName, lastException, context.RetryCount);
            _jobsInstrumentation.LogJobCompleted(
                context.JobId,
                context.FunctionName,
                stopWatch.ElapsedMilliseconds,
                success: false
            );

            await _ObserveJobExceptionAsync(lastException, context, cancellationToken).ConfigureAwait(false);

            // Terminal-status write must persist regardless of host-stop/lease-loss (completion fence guards it).
            var affected = await _internalJobsManager
                .UpdateTickerAsync(context, CancellationToken.None)
                .ConfigureAwait(false);
            if (affected > 0)
            {
                // The terminal row no longer owns a renewable lease. Stop renewal before invoking
                // user code so the renewal loop cannot mistake the expected terminal fence for loss.
                await stopRenewalAsync().ConfigureAwait(false);
            }

            var retryBudget = Math.Min(context.Retries, _retryOptions.RetryStrategy.MaxRetryAttempts);
            if (affected > 0 && lastFailureRetryable && context.RetryCount >= retryBudget)
            {
                await _InvokeOnExhaustedAsync(context, lastException, cancellationToken).ConfigureAwait(false);
            }
        }

        // Stop renewal before disposing the job CTS it cancels on loss.
        await stopRenewalAsync().ConfigureAwait(false);

        // IMPORTANT: Always dispose CancellationTokenSource to prevent memory leaks
        cancellationTokenSource?.Dispose();
        JobsCancellationTokenManager.RemoveTickerCancellationToken(context.JobId);
    }

    private async ValueTask _ObserveJobExceptionAsync(
        Exception exception,
        JobExecutionState context,
        CancellationToken cancellationToken
    )
    {
        if (_serviceProvider.GetService(typeof(IJobExceptionHandler)) is not IJobExceptionHandler handler)
        {
            return;
        }

        // Bound the handler with the same hard-timeout + orphan-handling mechanics as _InvokeOnExhaustedAsync: on the
        // per-retry path this call runs inline before the pipeline computes the next delay, so a slow / hanging
        // IJobExceptionHandler would otherwise stall every retry of a failing job until lease loss. The linked token is
        // passed to the handler and cancelled on timeout so a cooperative handler can short-circuit; the CTS is
        // disposed only once an orphaned handler can no longer observe it (deferred continuation), never underneath it.
        var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? handlerTask = null;
        try
        {
            handlerTask = handler.HandleExceptionAsync(exception, context.JobId, context.Type, handlerCts.Token);
            await handlerTask
                .WaitAsync(_retryOptions.OnExhaustedTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await handlerCts.CancelAsync().ConfigureAwait(false);
            _logger.LogJobExceptionObserverTimedOut(
                context.JobId,
                context.FunctionName,
                _retryOptions.OnExhaustedTimeout.TotalSeconds
            );
        }
        catch (Exception observerException)
        {
            _logger.LogJobExceptionObserverFailed(observerException, context.JobId, context.FunctionName);
        }
        finally
        {
            if (handlerTask is { IsCompleted: false })
            {
                // A handler that ignores cancellation may still observe handlerCts.Token. Transfer ownership until it
                // completes instead of disposing the linked CTS underneath it (mirrors _InvokeOnExhaustedAsync).
#pragma warning disable CA2025 // The continuation owns and disposes the captured CTS after handler completion.
                _ = _DisposeObserverResourcesAfterCompletionAsync(handlerTask, handlerCts);
#pragma warning restore CA2025
            }
            else
            {
                handlerCts.Dispose();
            }
        }
    }

    private static async Task _DisposeObserverResourcesAfterCompletionAsync(
        Task handlerTask,
        CancellationTokenSource handlerCts
    )
    {
#pragma warning disable VSTHRD003 // The task is user handler work intentionally observed by this ownership continuation.
#pragma warning disable ERP022 // The bounded caller already logged handler failure; this continuation only releases the CTS.
        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch
        {
            // Failure was already logged by the bounded caller.
        }
        finally
        {
            handlerCts.Dispose();
        }
#pragma warning restore ERP022
#pragma warning restore VSTHRD003
    }

    private async Task _InvokeOnExhaustedAsync(
        JobExecutionState context,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        if (_retryOptions.OnExhausted is null)
        {
            return;
        }

        var scope = _serviceProvider.CreateAsyncScope();
        var callbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? callbackTask = null;
        try
        {
            callbackTask = _retryOptions.OnExhausted(
                new JobExhaustedContext(
                    context.JobId,
                    context.FunctionName,
                    context.Type,
                    exception,
                    context.RetryCount,
                    scope.ServiceProvider
                ),
                callbackCts.Token
            );
            await callbackTask
                .WaitAsync(_retryOptions.OnExhaustedTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException timeoutException)
        {
            await callbackCts.CancelAsync().ConfigureAwait(false);
            _logger.LogJobExhaustedCallbackFailed(timeoutException, context.JobId, context.FunctionName);
        }
        catch (Exception callbackException)
        {
            _logger.LogJobExhaustedCallbackFailed(callbackException, context.JobId, context.FunctionName);
        }
        finally
        {
            if (callbackTask is { IsCompleted: false })
            {
                // A callback that ignores cancellation may still be using scoped services. Transfer
                // ownership until it completes instead of disposing resources underneath user code.
#pragma warning disable CA2025 // The continuation owns and disposes all captured resources after callback completion.
                _ = _DisposeCallbackResourcesAfterCompletionAsync(callbackTask, scope, callbackCts);
#pragma warning restore CA2025
            }
            else
            {
                callbackCts.Dispose();
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task _DisposeCallbackResourcesAfterCompletionAsync(
        Task callbackTask,
        AsyncServiceScope scope,
        CancellationTokenSource callbackCts
    )
    {
#pragma warning disable VSTHRD003 // The task is user callback work intentionally observed by this ownership continuation.
#pragma warning disable ERP022 // The bounded caller already logged callback failure; this continuation only releases resources.
        try
        {
            await callbackTask.ConfigureAwait(false);
        }
        catch
        {
            // Failure was already logged by the bounded caller.
        }
        finally
        {
            callbackCts.Dispose();
            await scope.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning restore ERP022
#pragma warning restore VSTHRD003
    }

    private async Task _RenewLeaseLoopAsync(
        JobExecutionState context,
        CancellationTokenSource jobCts,
        CancellationToken renewalLoopToken
    )
    {
        // #316 sliding-lease renewal loop (U1 renewal + U2 cancel-on-loss). Runs concurrently with the job for its
        // whole execution and stops when renewalLoopToken is cancelled by StopRenewalAsync on completion.
        // The lease was freshly stamped when the job was claimed (≈ loop start), so seed the last-confirmed time here:
        // it bounds how long a membership blip may be tolerated before the lease has certainly lapsed (#461).
        var lastConfirmedAt = _timeProvider.GetUtcNow();
        try
        {
            while (!renewalLoopToken.IsCancellationRequested)
            {
                // Delay BEFORE the first renew: a job finishing within one cadence writes no renewal.
                await _timeProvider.Delay(_leaseRenewalInterval, renewalLoopToken).ConfigureAwait(false);

                var outcome = await _TryRenewLeaseAsync(context, renewalLoopToken).ConfigureAwait(false);

                if (outcome is RenewalOutcome.Held)
                {
                    lastConfirmedAt = _timeProvider.GetUtcNow();
                    continue;
                }

                if (
                    outcome is RenewalOutcome.MembershipUnknown
                    && _timeProvider.GetUtcNow() - lastConfirmedAt < _leaseDuration
                )
                {
                    // #461: coordination membership is momentarily unestablished (registration pending / transient
                    // blip) — distinct from a lost lease. While still inside the lease window, skip this tick and keep
                    // the healthy job running; renewal resumes once membership re-establishes. Logged at Debug since it
                    // can repeat during a partition.
                    _logger.LogJobLeaseRenewalSkippedMembershipUnknown(context.JobId, context.FunctionName);
                    continue;
                }

                // Lease lost (reclaimed / owner changed / terminalized, or the store could not confirm within the
                // cadence — #463), OR membership has stayed unestablished for the whole lease window (#461 bound: the
                // lease has now lapsed and the row is being reclaimed elsewhere, so stop the local zombie). Flag
                // lease-loss so the cancellation handler leaves the row InProgress for the stalled-reclaim/OnNodeDeath
                // sweep (#1) rather than writing a terminal Cancelled, then best-effort cancel the running job (a job
                // ignoring its token still relies on OnNodeDeath for correctness).
                context.LeaseLost = true;
                await jobCts.CancelAsync().ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected stop signal when renewalLoopToken fires on normal completion — not a failure.
        }
    }

    // Outcome of a single renewal attempt. Held -> keep running; Lost -> cancel-on-loss; MembershipUnknown ->
    // coordination membership not currently established (#461), skip this tick without cancelling.
    private enum RenewalOutcome
    {
        Held,
        Lost,
        MembershipUnknown,
    }

    /// <summary>
    /// One renewal attempt. <see cref="RenewalOutcome.Held"/> while the lease is still held;
    /// <see cref="RenewalOutcome.Lost"/> when it was lost OR the renewal could not be confirmed within the cadence
    /// (#463: a hung / slow / unreachable store, or an error, must not silently let the lease lapse — treated as a
    /// loss so the caller cancels and OnNodeDeath governs); <see cref="RenewalOutcome.MembershipUnknown"/> when
    /// coordination membership is not currently established (#461), which the caller skips rather than cancels.
    /// </summary>
    private async Task<RenewalOutcome> _TryRenewLeaseAsync(
        JobExecutionState context,
        CancellationToken renewalLoopToken
    )
    {
        // Bound the renewal DB call to one cadence so a hung store can't block the loop past the lease deadline.
        using var timeoutCts = new CancellationTokenSource(_leaseRenewalInterval, _timeProvider);
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(renewalLoopToken, timeoutCts.Token);

        try
        {
            var affected = await _internalJobsManager.RenewLeaseAsync(context, renewalCts.Token).ConfigureAwait(false);

            // >0 renewed (held); 0 the row matched no rows (genuinely lost); <0 the renew was gated because
            // coordination membership is not established (#461) — skip, not loss.
            return affected > 0 ? RenewalOutcome.Held
                : affected < 0 ? RenewalOutcome.MembershipUnknown
                : RenewalOutcome.Lost;
        }
        catch (OperationCanceledException) when (renewalLoopToken.IsCancellationRequested)
        {
            // Normal stop (job completed) raced the renewal — propagate so the loop's outer catch treats it as a
            // clean stop rather than a lease loss.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Renewal deadline elapsed: the store did not confirm the lease within the cadence. Treat as lost.
            return RenewalOutcome.Lost;
        }
        catch (Exception exception)
        {
            // Renewal write failed (DB unreachable / transient error). The two preceding catches already consume every
            // OperationCanceledException, so only non-OCE exceptions reach here. We can no longer vouch for the lease —
            // treat it as lost so OnNodeDeath governs correctness (Retry re-runs, MarkFailed/Skip stay terminal). Log
            // at Warning so a DB-error renewal loss is observable and distinguishable from a programming bug (#463/R2).
            _logger.LogJobLeaseRenewalFailed(exception, context.JobId, context.FunctionName);
            return RenewalOutcome.Lost;
        }
    }

    private async Task<bool> _VerifyLeaseBeforeExecutionAsync(
        JobExecutionState context,
        CancellationToken cancellationToken
    )
    {
        var outcome = await _TryRenewLeaseAsync(context, cancellationToken).ConfigureAwait(false);

        if (outcome is RenewalOutcome.Held)
        {
            return true;
        }

        if (outcome is RenewalOutcome.MembershipUnknown)
        {
            _logger.LogJobStartLeaseVerificationSkippedMembershipUnknown(context.JobId, context.FunctionName);

            return true;
        }

        context.LeaseLost = true;
        _logger.LogJobLeaseLostBeforeExecution(context.JobId, context.FunctionName);

        return false;
    }

    private static Exception _GetRootException(Exception ex)
    {
        while (ex.InnerException != null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    private static string _SerializeException(Exception ex)
    {
        var rootException = _GetRootException(ex);
        var stackTrace = new StackTrace(rootException, fNeedFileInfo: true);
        var frame = stackTrace.GetFrame(0);

        return JsonSerializer.Serialize(
            new ExceptionDetailClassForSerialization
            {
                Message = ex.Message,
                StackTrace = frame?.ToString() ?? rootException.StackTrace,
            }
        );
    }

    private static bool _ShouldRunChild(JobExecutionState childContext, JobStatus parentStatus)
    {
        return childContext.RunCondition switch
        {
            RunCondition.InProgress => parentStatus == JobStatus.InProgress,
            RunCondition.OnSuccess => parentStatus is JobStatus.Succeeded or JobStatus.DueDone,
            RunCondition.OnFailure => parentStatus == JobStatus.Failed,
            RunCondition.OnCancelled => parentStatus == JobStatus.Cancelled,
            RunCondition.OnFailureOrCancelled => parentStatus is JobStatus.Failed or JobStatus.Cancelled,
            RunCondition.OnAnyCompletedStatus => parentStatus
                is JobStatus.Succeeded
                    or JobStatus.DueDone
                    or JobStatus.Failed
                    or JobStatus.Cancelled,
            _ => false,
        };
    }

    private static void _GatherDescendantsToSkip(JobExecutionState parent, List<JobExecutionState> skipList)
    {
        if (parent.TimeJobChildren.Count == 0)
        {
            return;
        }

        foreach (var child in parent.TimeJobChildren)
        {
            skipList.Add(child);

            // Recursively gather grandchildren
            _GatherDescendantsToSkip(child, skipList);
        }
    }

    private async Task _SafeRecursiveExecution(
        JobExecutionState context,
        bool isDue,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Descendants are pre-leased with the root claim; transition each child to InProgress before its lease
            // fence and preserve that child identity recursively for grandchildren.
            await _ExecuteTaskAsync(context, isDue, isChild: true, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Scheduler must continue running if task execution throws outside status handling.
        catch (Exception exception)
        {
            // A throw outside ExecuteTaskAsync's normal status handling leaves the child InProgress until the
            // stalled-reclaim sweep recovers it per OnNodeDeath. Swallow so the parent scheduler loop survives, but
            // log at Warning so the otherwise-silent failure is observable (#465).
            _logger.LogJobChildExecutionThrewSynchronously(exception, context.JobId, context.FunctionName);
        }
#pragma warning restore ERP022
    }
}

internal static partial class JobsExecutionTaskHandlerLog
{
    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Warning,
        Message = "Lease renewal for job {JobId} ({Function}) failed; treating the lease as lost and cancelling the "
            + "running job. The row is left InProgress for the stalled-reclaim sweep to recover per OnNodeDeath."
    )]
    public static partial void LogJobLeaseRenewalFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Information,
        Message = "Job {JobId} ({Function}) was cancelled on lease loss; row left InProgress for the stalled-reclaim "
            + "sweep to recover per OnNodeDeath (no terminal status written)."
    )]
    public static partial void LogJobLeaseLostCancellation(this ILogger logger, Guid jobId, string function);

    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Warning,
        Message = "Child job {JobId} ({Function}) threw synchronously before execution started; it stays InProgress "
            + "until the stalled-reclaim sweep recovers it per OnNodeDeath. The parent scheduler loop continues."
    )]
    public static partial void LogJobChildExecutionThrewSynchronously(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Debug,
        Message = "Lease renewal for job {JobId} ({Function}) skipped: coordination membership is not currently "
            + "established. The job keeps running; the renewal will resume once membership re-establishes (#461)."
    )]
    public static partial void LogJobLeaseRenewalSkippedMembershipUnknown(
        this ILogger logger,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3104,
        Level = LogLevel.Warning,
        Message = "Job {JobId} ({Function}) completed successfully but its completion write matched 0 rows — the row "
            + "was reclaimed/terminalized by a sweep (likely a stall outlasting the lease). The durable record may "
            + "show a terminal failure that did not actually occur; reconcile before re-triggering (#462)."
    )]
    public static partial void LogJobCompletionFencedAfterSuccess(this ILogger logger, Guid jobId, string function);

    [LoggerMessage(
        EventId = 3105,
        Level = LogLevel.Warning,
        Message = "Job {JobId} ({Function}) lost ownership before execution started; user code was not invoked and "
            + "the row is left InProgress for the stalled-reclaim sweep to recover per OnNodeDeath."
    )]
    public static partial void LogJobLeaseLostBeforeExecution(this ILogger logger, Guid jobId, string function);

    [LoggerMessage(
        EventId = 3106,
        Level = LogLevel.Debug,
        Message = "Start lease verification for job {JobId} ({Function}) skipped: coordination membership is not "
            + "currently established. The job will start and the renewal loop will keep checking membership."
    )]
    public static partial void LogJobStartLeaseVerificationSkippedMembershipUnknown(
        this ILogger logger,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3107,
        Level = LogLevel.Warning,
        Message = "Job exception observer failed for job {JobId} ({Function}); retry and durable state continue."
    )]
    public static partial void LogJobExceptionObserverFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3108,
        Level = LogLevel.Warning,
        Message = "Jobs Polly retry observer failed for job {JobId} ({Function}); retry and durable state continue."
    )]
    public static partial void LogJobsRetryObserverFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3109,
        Level = LogLevel.Warning,
        Message = "Jobs exhausted callback failed or timed out for job {JobId} ({Function})."
    )]
    public static partial void LogJobExhaustedCallbackFailed(
        this ILogger logger,
        Exception exception,
        Guid jobId,
        string function
    );

    [LoggerMessage(
        EventId = 3110,
        Level = LogLevel.Warning,
        Message = "Job exception observer for job {JobId} ({Function}) exceeded its {TimeoutSeconds}s bound and was "
            + "cancelled; retry and durable state continue."
    )]
    public static partial void LogJobExceptionObserverTimedOut(
        this ILogger logger,
        Guid jobId,
        string function,
        double timeoutSeconds
    );
}
