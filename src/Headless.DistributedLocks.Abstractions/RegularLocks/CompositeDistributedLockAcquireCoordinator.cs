// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static class CompositeDistributedLockAcquireCoordinator
{
    private static readonly TimeSpan _MaxRenewalCadence = TimeSpan.FromMinutes(1);

    internal static async Task<CompositeDistributedLockAcquireResult> TryAcquireAsync(
        IDistributedLock provider,
        IEnumerable<string> resources,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(provider);
        Argument.IsNotNull(resources);

        var canonicalResources = _MaterializeCanonicalResources(resources);
        options ??= new DistributedLockAcquireOptions();

        var totalBudget = options.AcquireTimeout ?? provider.DefaultAcquireTimeout;

        // Mirrors DistributedLockCoreHelpers.ValidateAcquireTimeout, which this package cannot call:
        // that helper lives in Headless.DistributedLocks.Core, which references Abstractions, not the reverse.
        if (totalBudget != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositiveOrZero(totalBudget, paramName: nameof(options));
            Argument.IsLessThan(totalBudget.TotalMilliseconds, int.MaxValue, paramName: nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var timeProvider = provider.TimeProvider;
        var startedAt = timeProvider.GetTimestamp();
        var infiniteBudget = totalBudget == Timeout.InfiniteTimeSpan;
        var tryOnce = totalBudget == TimeSpan.Zero;
        using var deadlineSource =
            !infiniteBudget && !tryOnce ? timeProvider.CreateCancellationTokenSource(totalBudget) : null;
        using var operationSource = deadlineSource is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineSource.Token)
            : null;
        var operationToken = operationSource?.Token ?? cancellationToken;
        var acquired = new List<IDistributedLease>(canonicalResources.Length);
        var renewalSchedule = new FormationRenewalSchedule(timeProvider, _GetRenewalCadence(provider, options));
        CancellationTokenSource? formationLossSource = null;
        var formationLossRegistrations = new List<CancellationTokenRegistration>(canonicalResources.Length);

        try
        {
            foreach (var resource in canonicalResources)
            {
                if (!tryOnce && !infiniteBudget && _GetRemaining(timeProvider, startedAt, totalBudget) <= TimeSpan.Zero)
                {
                    return await _RollbackForTimeoutAsync(
                            acquired,
                            _GetCompositeResource(canonicalResources),
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
                        provider,
                        resource,
                        childOptions,
                        acquired,
                        renewalSchedule,
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
                            _GetCompositeResource(canonicalResources),
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
                            static state => ((CancellationTokenSource)state!).Cancel(),
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
                        _GetCompositeResource(canonicalResources),
                        cancellationToken,
                        deadlineSource.Token
                    )
                    .ConfigureAwait(false);
            }

            if (acquired.Count == 1)
            {
                return new CompositeDistributedLockAcquireResult(acquired[0], canonicalResources[0], tryOnce);
            }

            var compositeResource = _GetCompositeResource(canonicalResources);
#pragma warning disable CA2000 // Ownership is transferred to the returned IDistributedLease.
            var lease = new CompositeDistributedLease(
                acquired,
                compositeResource,
                timeProvider.GetUtcNow(),
                timeProvider.GetElapsedTime(startedAt),
                options.ReleaseOnDispose
            );
#pragma warning restore CA2000

            return new CompositeDistributedLockAcquireResult(lease, compositeResource, tryOnce);
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

            return new CompositeDistributedLockAcquireResult(null, _GetCompositeResource(canonicalResources), tryOnce);
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

    private static string[] _MaterializeCanonicalResources(IEnumerable<string> resources)
    {
        var materialized = new List<string>();

        foreach (var resource in resources)
        {
            materialized.Add(Argument.IsNotNullOrWhiteSpace(resource));
        }

        Argument.IsNotEmpty(materialized, paramName: nameof(resources));

        return
        [
            .. materialized
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static resource => resource, StringComparer.Ordinal),
        ];
    }

    private static async Task<IDistributedLease?> _AcquireChildAsync(
        IDistributedLock provider,
        string resource,
        DistributedLockAcquireOptions options,
        List<IDistributedLease> held,
        FormationRenewalSchedule renewalSchedule,
        CancellationToken formationLossToken,
        CancellationToken operationToken,
        CancellationToken callerToken,
        CancellationToken deadlineToken
    )
    {
        using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        var pendingAcquire = provider.TryAcquireAsync(resource, options, attemptSource.Token);

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
        using var operationRegistration = operationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            waitSignal
        );
        using var lossRegistration = formationLossToken.CanBeCanceled
            ? formationLossToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), waitSignal)
            : default;

        // NOTE (review finding #6): Task.WhenAny attaches a continuation to every task it is given and never removes
        // it from the losers, so re-racing pendingAcquire and waitSignal.Task on each cadence tick appends
        // continuations to them for the life of this acquisition. Hoisting the pendingAcquire/waitSignal race out of
        // the loop is the fix, but the hoisted task then outlives `attemptSource`'s `using` scope as far as CA2025
        // can prove, and this package does not suppress analyzers without sign-off. Left as-is pending that call.
        // The TCS sentinel above already removes two of the four racing tasks (and both timers).
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
                        cadenceTask = Task.Delay(cadenceDelay, provider.TimeProvider, cadenceSource.Token);
                    }
                    else
                    {
                        cadenceTask = Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
                    }

                    var completed = await Task.WhenAny(pendingAcquire, waitSignal.Task, cadenceTask)
                        .ConfigureAwait(false);

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
                        var renewalTask = _RenewHeldAsync(
                            held,
                            options.TimeUntilExpires ?? provider.DefaultTimeUntilExpires,
                            renewalSource.Token
                        );
                        var renewalCompleted = await Task.WhenAny(renewalTask, waitSignal.Task).ConfigureAwait(false);

                        if (renewalCompleted == renewalTask)
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

    private static TimeSpan? _GetRenewalCadence(IDistributedLock provider, DistributedLockAcquireOptions options)
    {
        var timeUntilExpires = options.TimeUntilExpires ?? provider.DefaultTimeUntilExpires;

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

    private static string _GetCompositeResource(IReadOnlyList<string> canonicalResources)
    {
        return string.Join("+", canonicalResources);
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

    private static async Task<CompositeDistributedLockAcquireResult> _RollbackForTimeoutAsync(
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

        return new CompositeDistributedLockAcquireResult(null, compositeResource, TryOnce: false);
    }

    private static async Task<CompositeDistributedLockAcquireResult> _RollbackForNullAsync(
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

        return new CompositeDistributedLockAcquireResult(null, compositeResource, tryOnce);
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

internal readonly record struct CompositeDistributedLockAcquireResult(
    IDistributedLease? Lease,
    string Resource,
    bool TryOnce
);
