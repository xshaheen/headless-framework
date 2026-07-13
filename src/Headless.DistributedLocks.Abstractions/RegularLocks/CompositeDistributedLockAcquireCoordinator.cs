// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
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
        var compositeResource = string.Join("+", canonicalResources);
        options ??= new DistributedLockAcquireOptions();

        var totalBudget = options.AcquireTimeout ?? provider.DefaultAcquireTimeout;

        if (totalBudget < TimeSpan.Zero && totalBudget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.AcquireTimeout,
                "AcquireTimeout must be non-negative or Timeout.InfiniteTimeSpan."
            );
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

        try
        {
            foreach (var resource in canonicalResources)
            {
                if (!tryOnce && !infiniteBudget && _GetRemaining(timeProvider, startedAt, totalBudget) <= TimeSpan.Zero)
                {
                    return await _RollbackForTimeoutAsync(
                            acquired,
                            compositeResource,
                            tryOnce,
                            cancellationToken,
                            deadlineSource!.Token
                        )
                        .ConfigureAwait(false);
                }

                var childOptions = options with
                {
                    AcquireTimeout =
                        tryOnce ? TimeSpan.Zero
                        : infiniteBudget ? Timeout.InfiniteTimeSpan
                        : _GetRemaining(timeProvider, startedAt, totalBudget),
                };

                var child = await _AcquireChildAsync(
                        provider,
                        resource,
                        childOptions,
                        acquired,
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
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (deadlineSource?.IsCancellationRequested == true)
            {
                return await _RollbackForTimeoutAsync(
                        acquired,
                        compositeResource,
                        tryOnce,
                        cancellationToken,
                        deadlineSource.Token
                    )
                    .ConfigureAwait(false);
            }

            if (acquired.Count == 1)
            {
                return new CompositeDistributedLockAcquireResult(acquired[0], compositeResource, tryOnce);
            }

#pragma warning disable CA2000 // Ownership is transferred to the returned IDistributedLease.
            var lease = new CompositeDistributedLease(
                acquired,
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

            return new CompositeDistributedLockAcquireResult(null, compositeResource, tryOnce);
        }
        catch (Exception exception)
        {
            var cleanupErrors = await _RollbackAsync(acquired).ConfigureAwait(false);

            if (cleanupErrors is not null)
            {
                _ThrowCombined(exception, cleanupErrors);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    private static string[] _MaterializeCanonicalResources(IEnumerable<string> resources)
    {
        var materialized = new List<string>();

        foreach (var resource in resources)
        {
            materialized.Add(Argument.IsNotNullOrWhiteSpace(resource));
        }

        if (materialized.Count == 0)
        {
            throw new ArgumentException("At least one resource is required.", nameof(resources));
        }

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

        var lossTokens = held.Where(static child => child.CanObserveLoss)
            .Select(static child => child.LostToken)
            .ToArray();
        using var lossSource =
            lossTokens.Length > 0 ? CancellationTokenSource.CreateLinkedTokenSource(lossTokens) : null;
        using var waitCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        var lossTask = lossSource is null
            ? Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None)
            : Task.Delay(Timeout.InfiniteTimeSpan, lossSource.Token);
        var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, waitCancellationSource.Token);
        var cadence = _GetRenewalCadence(provider, options);

        try
        {
            while (true)
            {
                CancellationTokenSource? cadenceSource = null;

                try
                {
                    Task cadenceTask;

                    if (cadence is { } cadenceValue)
                    {
#pragma warning disable CA2000 // Disposed by the enclosing iteration's finally block.
                        cadenceSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
#pragma warning restore CA2000
                        cadenceTask = Task.Delay(cadenceValue, provider.TimeProvider, cadenceSource.Token);
                    }
                    else
                    {
                        cadenceTask = Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
                    }

                    var completed = await Task.WhenAny(pendingAcquire, lossTask, cancellationTask, cadenceTask)
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

                    if (completed == pendingAcquire)
                    {
                        return await pendingAcquire.ConfigureAwait(false);
                    }

                    if (completed != cadenceTask)
                    {
                        continue;
                    }

                    using var renewalSource = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
                    var renewalTask = _RenewHeldAsync(
                        held,
                        options.TimeUntilExpires ?? provider.DefaultTimeUntilExpires,
                        renewalSource.Token
                    );
                    var renewalCompleted = await Task.WhenAny(renewalTask, lossTask, cancellationTask)
                        .ConfigureAwait(false);

                    if (renewalCompleted == renewalTask)
                    {
                        await renewalTask.ConfigureAwait(false);
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
            ExceptionDispatchInfo.Capture(observedFailure).Throw();
            throw;
        }
        finally
        {
            await waitCancellationSource.CancelAsync().ConfigureAwait(false);
            await _DrainCancelledDelayAsync(cancellationTask).ConfigureAwait(false);

            if (lossSource is not null)
            {
                await lossSource.CancelAsync().ConfigureAwait(false);
                await _DrainCancelledDelayAsync(lossTask).ConfigureAwait(false);
            }
        }
    }

    private static async Task _RenewHeldAsync(
        IReadOnlyList<IDistributedLease> held,
        TimeSpan timeUntilExpires,
        CancellationToken cancellationToken
    )
    {
        var tasks = held.Select(child => _RenewChildAsync(child, timeUntilExpires, cancellationToken)).ToArray();
        var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
        List<Exception>? errors = null;

        foreach (var outcome in outcomes)
        {
            if (outcome.Exception is { } exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        if (errors is not null)
        {
            if (
                cancellationToken.IsCancellationRequested
                && errors.All(static error => error is OperationCanceledException)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            CompositeDistributedLeaseOperations.ThrowIfAny(errors);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lostChild = outcomes.FirstOrDefault(static outcome => !outcome.Renewed).Child;

        if (lostChild is not null)
        {
            throw new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
        }
    }

    private static async Task<RenewChildOutcome> _RenewChildAsync(
        IDistributedLease child,
        TimeSpan timeUntilExpires,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return new RenewChildOutcome(
                child,
                await child.RenewAsync(timeUntilExpires, cancellationToken).ConfigureAwait(false),
                Exception: null
            );
        }
        catch (Exception exception)
        {
            return new RenewChildOutcome(child, Renewed: false, exception);
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

        var half = TimeSpan.FromTicks(Math.Max(1, timeUntilExpires.Ticks / 2));
        return half <= _MaxRenewalCadence ? half : _MaxRenewalCadence;
    }

    private static TimeSpan _GetRemaining(TimeProvider timeProvider, long startedAt, TimeSpan totalBudget)
    {
        var remaining = totalBudget - timeProvider.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
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

#pragma warning disable VSTHRD003 // These helpers explicitly cancel and drain operation tasks before ownership ends.
    private static async Task<Exception> _CancelAndObservePendingAsync(
        Task<IDistributedLease?> pendingAcquire,
        CancellationTokenSource attemptSource,
        List<IDistributedLease> acquired,
        Exception primary
    )
    {
        await attemptSource.CancelAsync().ConfigureAwait(false);

        try
        {
            var lateChild = await pendingAcquire.ConfigureAwait(false);

            if (lateChild is not null)
            {
                acquired.Add(lateChild);
            }

            return primary;
        }
        catch (OperationCanceledException) when (attemptSource.IsCancellationRequested)
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
        bool tryOnce,
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

        return new CompositeDistributedLockAcquireResult(null, compositeResource, tryOnce);
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
        Exception? cancellation =
            callerToken.IsCancellationRequested ? new OperationCanceledException(callerToken)
            : deadlineToken.IsCancellationRequested ? new OperationCanceledException(deadlineToken)
            : null;

        if (cleanupErrors is not null)
        {
            if (cancellation is not null)
            {
                _ThrowCombined(cancellation, cleanupErrors);
            }

            CompositeDistributedLeaseOperations.ThrowIfAny(cleanupErrors);
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

    private readonly record struct RenewChildOutcome(IDistributedLease Child, bool Renewed, Exception? Exception);
}

internal readonly record struct CompositeDistributedLockAcquireResult(
    IDistributedLease? Lease,
    string Resource,
    bool TryOnce
);
