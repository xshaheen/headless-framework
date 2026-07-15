// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Provider-agnostic all-or-nothing acquisition of a canonical request set under one acquire budget. Shared by the
/// mutex, reader-writer, and semaphore composites; each supplies its own canonicalization and child-acquire delegate.
/// </summary>
/// <remarks>
/// <para>
/// The caller canonicalizes — validate, dedupe, collapse, and ordinal-sort by resource — <em>before</em> entering the
/// coordinator, because the rules differ per primitive (the reader-writer set collapses <c>(a, Read)</c> and
/// <c>(a, Write)</c> to <c>(a, Write)</c>; the semaphore set rejects a duplicate resource with a conflicting
/// <c>maxCount</c>). What must not differ is the ordering: acquisition order is ordinal <em>by resource</em>, which is
/// what makes a composite unable to deadlock against another composite over overlapping names.
/// </para>
/// <para>
/// Cleanup has two distinct paths and they are not interchangeable. Rollback after a <em>failed</em> acquisition
/// <em>surfaces</em> cleanup failures — an <see cref="AggregateException"/> carrying the primary failure plus every
/// cleanup error, or <see cref="LockCleanupFailedException"/> when there is no primary. Only disposal of a
/// <em>successfully returned</em> <see cref="CompositeDistributedLease"/> swallows and logs, because it runs from a
/// <see langword="finally"/> where a throw would replace the caller's in-flight exception.
/// </para>
/// </remarks>
internal static class CompositeAcquireCoordinator
{
    private static readonly TimeSpan _MaxRenewalCadence = TimeSpan.FromMinutes(1);

    /// <summary>Acquires every request in <paramref name="canonicalRequests"/> under one shared acquire budget.</summary>
    /// <param name="canonicalRequests">The already-canonicalized request set, in acquisition order.</param>
    /// <param name="resourceOf">Projects a request to its resource name.</param>
    /// <param name="tryAcquireChild">Acquires one child; returns <see langword="null"/> on contention or timeout.</param>
    /// <param name="compositeResourceOf">
    /// Builds the joined diagnostic identity for a set of two or more. Only invoked in that case: a canonical set of
    /// one is not a composite, so it identifies itself by its bare resource name on every path — success and failure
    /// alike. Passing the joined form here instead would let a synthetic name (the reader-writer mode prefix in
    /// particular) leak into the timeout exception of what the caller sees as a single-resource acquire.
    /// </param>
    /// <param name="environment">The provider's clock, logger, and defaults.</param>
    /// <param name="options">Per-call configuration shared by every child acquisition.</param>
    /// <param name="cancellationToken">Cancels formation; pending child work is cancelled and drained first.</param>
    internal static async Task<CompositeAcquireResult> TryAcquireAsync<TRequest>(
        IReadOnlyList<TRequest> canonicalRequests,
        Func<TRequest, string> resourceOf,
        Func<TRequest, DistributedLockAcquireOptions, CancellationToken, Task<IDistributedLease?>> tryAcquireChild,
        Func<IReadOnlyList<TRequest>, string> compositeResourceOf,
        CompositeAcquireEnvironment environment,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(canonicalRequests);
        Argument.IsNotNull(resourceOf);
        Argument.IsNotNull(tryAcquireChild);
        Argument.IsNotNull(compositeResourceOf);
        Argument.IsNotEmpty(canonicalRequests);

        var compositeResource =
            canonicalRequests.Count == 1 ? resourceOf(canonicalRequests[0]) : compositeResourceOf(canonicalRequests);

        Argument.IsNotNullOrWhiteSpace(compositeResource);

        options ??= new DistributedLockAcquireOptions();

        var totalBudget = options.AcquireTimeout ?? environment.DefaultAcquireTimeout;

        // Mirrors DistributedLockCoreHelpers.ValidateAcquireTimeout, which this package cannot call:
        // that helper lives in Headless.DistributedLocks.Core, which references Abstractions, not the reverse.
        if (totalBudget != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositiveOrZero(totalBudget, paramName: nameof(options));
            Argument.IsLessThan(totalBudget.TotalMilliseconds, int.MaxValue, paramName: nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = environment.TimeProvider;
        var startedAt = timeProvider.GetTimestamp();
        var infiniteBudget = totalBudget == Timeout.InfiniteTimeSpan;
        var tryOnce = totalBudget == TimeSpan.Zero;
        using var deadlineSource =
            !infiniteBudget && !tryOnce ? timeProvider.CreateCancellationTokenSource(totalBudget) : null;
        using var operationSource = deadlineSource is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineSource.Token)
            : null;
        var operationToken = operationSource?.Token ?? cancellationToken;
        var acquired = new List<IDistributedLease>(canonicalRequests.Count);
        var renewalSchedule = new FormationRenewalSchedule(timeProvider, _GetRenewalCadence(environment, options));
        CancellationTokenSource? formationLossSource = null;
        var formationLossRegistrations = new List<CancellationTokenRegistration>(canonicalRequests.Count);

        try
        {
            foreach (var request in canonicalRequests)
            {
                if (!tryOnce && !infiniteBudget && _GetRemaining(timeProvider, startedAt, totalBudget) <= TimeSpan.Zero)
                {
                    return await _RollbackForTimeoutAsync(
                            acquired,
                            compositeResource,
                            cancellationToken,
                            deadlineSource!.Token
                        )
                        .ConfigureAwait(false);
                }

                var childOptions = options with
                {
                    AcquireTimeout = _GetChildAcquireTimeout(
                        tryOnce,
                        infiniteBudget,
                        timeProvider,
                        startedAt,
                        totalBudget
                    ),
                };

                var child = await _AcquireChildAsync(
                        request,
                        tryAcquireChild,
                        childOptions,
                        acquired,
                        renewalSchedule,
                        environment,
                        formationLossSource?.Token ?? CancellationToken.None,
                        operationToken,
                        cancellationToken,
                        deadlineSource?.Token ?? CancellationToken.None
                    )
                    .ConfigureAwait(false);

                if (child is null)
                {
                    return await _RollbackForNullAsync(
                            acquired,
                            compositeResource,
                            tryOnce,
                            cancellationToken,
                            deadlineSource?.Token ?? CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }

                acquired.Add(child);
                renewalSchedule.Start();

                if (child.CanObserveLoss)
                {
                    formationLossSource ??= new CancellationTokenSource();
                    formationLossRegistrations.Add(
                        child.LostToken.Register(
#pragma warning disable MA0045 // CancellationToken.Register requires a synchronous callback.
                            static state => ((CancellationTokenSource)state!).Cancel(),
#pragma warning restore MA0045
                            formationLossSource
                        )
                    );
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var lostChild = acquired.FirstOrDefault(static child => child.IsLost);

            if (lostChild is not null)
            {
                throw new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
            }

            if (deadlineSource?.IsCancellationRequested == true)
            {
                return await _RollbackForTimeoutAsync(
                        acquired,
                        compositeResource,
                        cancellationToken,
                        deadlineSource.Token
                    )
                    .ConfigureAwait(false);
            }

            // CompositeDistributedLease requires two or more children. A canonical set of one is not a composite:
            // return the provider's own lease so its real LeaseId and FencingToken survive. compositeResource is
            // already the bare resource name in that case, which is what keeps the failure paths consistent with this.
            if (acquired.Count == 1)
            {
                return new CompositeAcquireResult(acquired[0], compositeResource, tryOnce);
            }

#pragma warning disable CA2000 // Ownership is transferred to the returned IDistributedLease.
            var lease = new CompositeDistributedLease(
                acquired,
                compositeResource,
                timeProvider.GetUtcNow(),
                timeProvider.GetElapsedTime(startedAt),
                options.ReleaseOnDispose,
                environment.Logger
            );
#pragma warning restore CA2000

            return new CompositeAcquireResult(lease, compositeResource, tryOnce);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested || deadlineSource?.IsCancellationRequested == true)
        {
            var cleanupErrors = await _RollbackAsync(acquired).ConfigureAwait(false);
            var primary = cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancellationToken)
                : exception;

            if (cleanupErrors is not null)
            {
                _ThrowCombined(primary, cleanupErrors);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return new CompositeAcquireResult(Lease: null, Resource: compositeResource, TryOnce: tryOnce);
        }
        catch (Exception exception)
        {
            var cleanupErrors = await _RollbackAsync(acquired).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                var secondaryErrors = _GetCallerCancellationSecondaries(exception, cancellationToken);

                if (cleanupErrors is not null)
                {
                    secondaryErrors.AddRange(cleanupErrors);
                }

                if (secondaryErrors.Count > 0)
                {
                    _ThrowCombined(new OperationCanceledException(cancellationToken), secondaryErrors);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            if (cleanupErrors is not null)
            {
                _ThrowCombined(exception, cleanupErrors);
            }

            throw exception.ReThrow();
        }
        finally
        {
            foreach (var registration in formationLossRegistrations)
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }

            formationLossSource?.Dispose();
        }
    }

    private static async Task<IDistributedLease?> _AcquireChildAsync<TRequest>(
        TRequest request,
        Func<TRequest, DistributedLockAcquireOptions, CancellationToken, Task<IDistributedLease?>> tryAcquireChild,
        DistributedLockAcquireOptions options,
        List<IDistributedLease> held,
        FormationRenewalSchedule renewalSchedule,
        CompositeAcquireEnvironment environment,
        CancellationToken formationLossToken,
        CancellationToken operationToken,
        CancellationToken callerToken,
        CancellationToken deadlineToken
    )
    {
        // `using var` disposes at method exit -- after the catch below has cancelled and drained pendingAcquire, and
        // after the success path has awaited it. The acquire is therefore never in flight when the source backing its
        // token is disposed.
        using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        var pendingAcquire = tryAcquireChild(request, options, attemptSource.Token);

        if (held.Count == 0)
        {
            return await pendingAcquire.ConfigureAwait(false);
        }

        // One sentinel, not two: held-child loss and operation cancellation (caller or deadline) both mean "stop
        // waiting and re-derive why", and the post-wait checks below classify the cause from state rather than from
        // which task won. A TaskCompletionSource signalled by one-time token registrations replaces the pair of
        // infinite Task.Delay sentinels: there is no timer to schedule and nothing to cancel-and-drain, and the
        // signal cannot fault, so the wait arm never needs exception handling.
        var waitSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var operationRegistration = operationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            waitSignal
        );

        await using var lossRegistration = formationLossToken.CanBeCanceled
            ? formationLossToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), waitSignal)
            : default;

        // Hoisted once. Task.WhenAny attaches a continuation to every task it is given and never removes it from the
        // losers, so re-racing pendingAcquire and waitSignal.Task on every cadence tick would append continuations to
        // both for the life of the acquisition -- a short TTL against a long contended wait is hundreds of ticks.
        // Composing the race once and re-racing only the per-tick cadence against it keeps that growth off them.
        //
        // CA2025 flags this because the race is stored rather than awaited inline, so it cannot see that the task
        // completes before attemptSource is disposed. It does: `using var attemptSource` disposes at method exit,
        // and by then every exit path has settled pendingAcquire -- the success path awaits it, the failure path
        // cancels and drains it via _CancelAndObservePendingAsync (which also captures a handle that wins the
        // cancellation race, so a late acquire is never orphaned). pendingWinner completes with it. Suppressed on
        // that guarantee, not to silence the rule.
#pragma warning disable CA2025 // Disposed at method exit, after pendingAcquire is awaited or drained on every path.
        var pendingWinner = Task.WhenAny(pendingAcquire, waitSignal.Task);
#pragma warning restore CA2025

        try
        {
            while (true)
            {
                CancellationTokenSource? cadenceSource = null;

                try
                {
                    Task cadenceTask;

                    if (renewalSchedule.GetDelay() is { } cadenceDelay)
                    {
#pragma warning disable CA2000 // Disposed by the enclosing iteration's finally block.
                        cadenceSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
#pragma warning restore CA2000
                        cadenceTask = Task.Delay(cadenceDelay, environment.TimeProvider, cadenceSource.Token);
                    }
                    else
                    {
                        cadenceTask = Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            environment.TimeProvider,
                            CancellationToken.None
                        );
                    }

                    var completed = await Task.WhenAny(pendingWinner, cadenceTask).ConfigureAwait(false);

                    if (cadenceSource is not null && completed != cadenceTask)
                    {
                        await _CancelAndDrainDelayAsync(cadenceTask, cadenceSource).ConfigureAwait(false);
                    }

                    callerToken.ThrowIfCancellationRequested();

                    var lostChild = held.FirstOrDefault(static child => child.IsLost);

                    if (lostChild is not null)
                    {
                        throw new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
                    }

                    if (deadlineToken.IsCancellationRequested || operationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(operationToken);
                    }

                    if (pendingAcquire.IsCompleted)
                    {
                        return await pendingAcquire.ConfigureAwait(false);
                    }

                    if (completed == cadenceTask)
                    {
                        using var renewalSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);

                        // The REQUESTED TTL, not the effective one that set the cadence. Each child re-applies its own
                        // backend's clamp on renewal exactly as it did on acquire, so a mixed reader-writer set renews
                        // its read child to the finite default and its write child to the infinite lease the caller
                        // actually asked for. Passing the clamped value here would silently downgrade the write child.
                        var renewalTask = _RenewHeldAsync(
                            held,
                            options.TimeUntilExpires ?? environment.DefaultTimeUntilExpires,
                            renewalSource.Token
                        );
                        var renewalCompleted = await Task.WhenAny(renewalTask, pendingWinner).ConfigureAwait(false);

                        if (renewalCompleted == renewalTask)
                        {
                            await renewalTask.ConfigureAwait(false);
                            renewalSchedule.Reset();
                            continue;
                        }

                        // pendingWinner also completes when the child is simply ACQUIRED -- it races the pending
                        // acquire, not just the loss/cancellation sentinel. Winning this race is therefore not by
                        // itself an interruption, and treating it as one would abort a composite whose last child
                        // landed inside the renewal round-trip. Only a genuinely interrupted wait aborts; otherwise
                        // finish the renewal (so a real renewal failure still surfaces) and let the loop head hand
                        // the completed child back.
                        if (
                            !callerToken.IsCancellationRequested
                            && !deadlineToken.IsCancellationRequested
                            && !operationToken.IsCancellationRequested
                            && held.FirstOrDefault(static child => child.IsLost) is null
                        )
                        {
                            await renewalTask.ConfigureAwait(false);
                            renewalSchedule.Reset();
                            continue;
                        }

                        var renewalPrimary = _CreateWaitInterruption(held, callerToken, deadlineToken, operationToken);
                        var observedRenewalFailure = await _CancelAndObserveTaskAsync(
                                renewalTask,
                                renewalSource,
                                renewalPrimary
                            )
                            .ConfigureAwait(false);
                        throw observedRenewalFailure;
                    }

                    // The wait sentinel fired, yet no held child reports loss and no token is cancelled. Nothing
                    // will change on a re-race, so looping would spin hot against an already-completed task. This
                    // is only reachable from a lease whose LostToken and IsLost disagree; treat it as a wait
                    // interruption rather than burning the remaining budget.
                    throw _CreateWaitInterruption(held, callerToken, deadlineToken, operationToken);
                }
                finally
                {
                    cadenceSource?.Dispose();
                }
            }
        }
        catch (Exception primary)
        {
            var observedFailure = await _CancelAndObservePendingAsync(pendingAcquire, attemptSource, held, primary)
                .ConfigureAwait(false);
            throw observedFailure.ReThrow();
        }
    }

    private static async Task _RenewHeldAsync(
        IReadOnlyList<IDistributedLease> held,
        TimeSpan timeUntilExpires,
        CancellationToken cancellationToken
    )
    {
        var outcomes = await CompositeDistributedLeaseOperations
            .CollectRenewalOutcomesAsync(held, child => child.RenewAsync(timeUntilExpires, cancellationToken))
            .ConfigureAwait(false);
        CompositeDistributedLeaseOperations.ThrowRenewalErrors(outcomes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var lostChild = outcomes.FirstOrDefault(static outcome => !outcome.Renewed).Child;

        if (lostChild is not null)
        {
            throw new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
        }
    }

    /// <summary>
    /// How often held children are renewed while a later child is still pending — half the lease TTL, capped at one
    /// minute; <see langword="null"/> means never. The TTL here is the <em>effective</em> one
    /// (<see cref="CompositeAcquireEnvironment.GetEffectiveTimeUntilExpires"/>), not the requested one: a reader-writer
    /// read child asked for an infinite lease really holds a finite one, and scheduling off the option would leave it
    /// unrenewed until it expired underneath a still-forming composite.
    /// </summary>
    private static TimeSpan? _GetRenewalCadence(
        CompositeAcquireEnvironment environment,
        DistributedLockAcquireOptions options
    )
    {
        var timeUntilExpires = environment.GetEffectiveTimeUntilExpires(options);

        if (
            options.Monitoring == LockMonitoringMode.AutoExtend
            || timeUntilExpires == Timeout.InfiniteTimeSpan
            || timeUntilExpires <= TimeSpan.Zero
        )
        {
            return null;
        }

        return TimeSpan.FromTicks(timeUntilExpires.Ticks / 2).Clamp(TimeSpan.FromTicks(1), _MaxRenewalCadence);
    }

    private static TimeSpan _GetRemaining(TimeProvider timeProvider, long startedAt, TimeSpan totalBudget)
    {
        return (totalBudget - timeProvider.GetElapsedTime(startedAt)).Max(TimeSpan.Zero);
    }

    private static TimeSpan _GetChildAcquireTimeout(
        bool tryOnce,
        bool infiniteBudget,
        TimeProvider timeProvider,
        long startedAt,
        TimeSpan totalBudget
    )
    {
        if (tryOnce)
        {
            return TimeSpan.Zero;
        }

        return infiniteBudget ? Timeout.InfiniteTimeSpan : _GetRemaining(timeProvider, startedAt, totalBudget);
    }

    private static Exception _CreateWaitInterruption(
        IReadOnlyList<IDistributedLease> held,
        CancellationToken callerToken,
        CancellationToken deadlineToken,
        CancellationToken operationToken
    )
    {
        if (callerToken.IsCancellationRequested)
        {
            return new OperationCanceledException(callerToken);
        }

        var lostChild = held.FirstOrDefault(static child => child.IsLost);

        if (lostChild is not null)
        {
            return new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
        }

        return new OperationCanceledException(deadlineToken.IsCancellationRequested ? deadlineToken : operationToken);
    }

    private static List<Exception> _GetCallerCancellationSecondaries(Exception exception, CancellationToken callerToken)
    {
        List<Exception>? flattened = null;
        _AddFlattened(ref flattened, exception);
        flattened!.RemoveAll(error =>
            error is OperationCanceledException cancellation && cancellation.CancellationToken == callerToken
        );
        return flattened;
    }

    private static void _AddFlattened(ref List<Exception>? errors, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            (errors ??= []).AddRange(aggregate.Flatten().InnerExceptions);
            return;
        }

        (errors ??= []).Add(exception);
    }

#pragma warning disable VSTHRD003 // These helpers explicitly cancel and drain operation tasks before ownership ends.
    /// <summary>
    /// Cancels and drains the pending child acquire, capturing a handle that wins the cancellation race so rollback
    /// can still release it — an acquire that completes after we stop waiting would otherwise leave the lock held
    /// with no reference to release it. The cancel/drain/exception-mapping ladder itself lives in
    /// <see cref="_CancelAndObserveTaskAsync"/>; keeping one copy of it means this path and the renewal path cannot
    /// silently diverge.
    /// </summary>
    private static Task<Exception> _CancelAndObservePendingAsync(
        Task<IDistributedLease?> pendingAcquire,
        CancellationTokenSource attemptSource,
        List<IDistributedLease> acquired,
        Exception primary
    )
    {
        return _CancelAndObserveTaskAsync(_CaptureLateChildAsync(pendingAcquire, acquired), attemptSource, primary);
    }

    private static async Task _CaptureLateChildAsync(
        Task<IDistributedLease?> pendingAcquire,
        List<IDistributedLease> acquired
    )
    {
        var lateChild = await pendingAcquire.ConfigureAwait(false);

        if (lateChild is not null)
        {
            acquired.Add(lateChild);
        }
    }

    private static async Task<Exception> _CancelAndObserveTaskAsync(
        Task task,
        CancellationTokenSource cancellationSource,
        Exception primary
    )
    {
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await task.ConfigureAwait(false);
            return primary;
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            return primary;
        }
        catch (Exception drainException) when (ReferenceEquals(primary, drainException))
        {
            return primary;
        }
        catch (Exception drainException)
        {
            return new AggregateException(primary, drainException);
        }
    }

    private static async Task _CancelAndDrainDelayAsync(Task delayTask, CancellationTokenSource cancellationSource)
    {
        await cancellationSource.CancelAsync().ConfigureAwait(false);
        await _DrainCancelledDelayAsync(delayTask).ConfigureAwait(false);
    }

    private static async Task _DrainCancelledDelayAsync(Task delayTask)
    {
        try
        {
            await delayTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
#pragma warning restore VSTHRD003

    private static async Task<CompositeAcquireResult> _RollbackForTimeoutAsync(
        List<IDistributedLease> acquired,
        string compositeResource,
        CancellationToken callerToken,
        CancellationToken deadlineToken
    )
    {
        var cleanupErrors = await _RollbackAsync(acquired).ConfigureAwait(false);

        if (callerToken.IsCancellationRequested)
        {
            var cancellation = new OperationCanceledException(callerToken);

            if (cleanupErrors is not null)
            {
                _ThrowCombined(cancellation, cleanupErrors);
            }

            callerToken.ThrowIfCancellationRequested();
        }

        if (cleanupErrors is not null)
        {
            _ThrowCombined(new OperationCanceledException(deadlineToken), cleanupErrors);
        }

        return new CompositeAcquireResult(Lease: null, Resource: compositeResource, TryOnce: false);
    }

    private static async Task<CompositeAcquireResult> _RollbackForNullAsync(
        List<IDistributedLease> acquired,
        string compositeResource,
        bool tryOnce,
        CancellationToken callerToken,
        CancellationToken deadlineToken
    )
    {
        var cleanupErrors = await _RollbackAsync(acquired).ConfigureAwait(false);
        Exception? cancellation = null;

        if (callerToken.IsCancellationRequested)
        {
            cancellation = new OperationCanceledException(callerToken);
        }
        else if (deadlineToken.IsCancellationRequested)
        {
            cancellation = new OperationCanceledException(deadlineToken);
        }

        if (cleanupErrors is not null)
        {
            if (cancellation is not null)
            {
                _ThrowCombined(cancellation, cleanupErrors);
            }

            CompositeDistributedLeaseOperations.ThrowCleanupErrors(cleanupErrors);
        }

        callerToken.ThrowIfCancellationRequested();

        return new CompositeAcquireResult(Lease: null, Resource: compositeResource, TryOnce: tryOnce);
    }

    private static async Task<List<Exception>?> _RollbackAsync(List<IDistributedLease> acquired)
    {
        List<Exception>? errors = null;

        for (var index = acquired.Count - 1; index >= 0; index--)
        {
            var child = acquired[index];

            try
            {
                await child.ReleaseAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }

            try
            {
                await child.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        acquired.Clear();
        return errors;
    }

    private static void _ThrowCombined(Exception primary, IReadOnlyCollection<Exception> cleanupErrors)
    {
        throw new AggregateException([primary, .. cleanupErrors]);
    }

    private sealed class FormationRenewalSchedule(TimeProvider timeProvider, TimeSpan? cadence)
    {
        private long? _lastRenewedAt;

        internal void Start()
        {
            if (cadence is not null)
            {
                _lastRenewedAt ??= timeProvider.GetTimestamp();
            }
        }

        internal TimeSpan? GetDelay()
        {
            return cadence is not { } value || _lastRenewedAt is not { } lastRenewedAt
                ? null
                : (value - timeProvider.GetElapsedTime(lastRenewedAt)).Max(TimeSpan.Zero);
        }

        internal void Reset()
        {
            if (cadence is not null)
            {
                _lastRenewedAt = timeProvider.GetTimestamp();
            }
        }
    }
}
