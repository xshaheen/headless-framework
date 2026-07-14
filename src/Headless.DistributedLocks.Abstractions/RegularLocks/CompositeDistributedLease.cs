// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal interface ICompositeDistributedLease
{
    IReadOnlyList<IDistributedLease> Children { get; }
}

internal sealed class CompositeDistributedLease : IDistributedLease, ICompositeDistributedLease
{
    private readonly IDistributedLease[] _children;
    private readonly bool _releaseOnDispose;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource? _lostSource;
    // The contract deliberately permits renew/release after DisposeAsync when release-on-dispose is false,
    // so disposing this gate as part of DisposeAsync would introduce a lifecycle race.
#pragma warning disable CA2213
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
#pragma warning restore CA2213
    private readonly Lock _disposeLock = new();

    private Task? _disposeTask;
    private int _canObserveLoss;
    private bool _isReleased;

    internal CompositeDistributedLease(
        IReadOnlyList<IDistributedLease> children,
        string resource,
        DateTimeOffset dateAcquired,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        ILogger logger
    )
    {
        Argument.IsNotNull(children);
        Argument.IsGreaterThanOrEqualTo(children.Count, 2);
        Argument.IsNotNull(logger);

        _children = [.. children];
        _releaseOnDispose = releaseOnDispose;
        _logger = logger;

        var canObserveLoss = _children[0].CanObserveLoss;

        if (_children.Any(child => child.CanObserveLoss != canObserveLoss))
        {
            throw new InvalidOperationException("All child leases must have the same loss-observability contract.");
        }

        if (canObserveLoss)
        {
            var lostTokens = _children.Select(child => child.LostToken).ToArray();

            if (lostTokens.Any(token => !token.CanBeCanceled))
            {
                throw new InvalidOperationException(
                    "A child lease that reports observable loss must expose a cancellable lost token."
                );
            }

            _lostSource = CancellationTokenSource.CreateLinkedTokenSource(lostTokens);

            if (_lostSource.IsCancellationRequested)
            {
                var lostChild = _children.First(static child => child.IsLost);
                _lostSource.Dispose();
                throw new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
            }
        }

        LeaseId = Guid.NewGuid().ToString("N");
        Resource = resource;
        DateAcquired = dateAcquired;
        TimeWaitedForLock = timeWaitedForLock;
        _canObserveLoss = canObserveLoss ? 1 : 0;
        LostTokenSignal = _lostSource?.Token ?? CancellationToken.None;
    }

    public string LeaseId { get; }

    public long? FencingToken => null;

    public string Resource { get; }

    public int RenewalCount => _children.Min(child => child.RenewalCount);

    public DateTimeOffset DateAcquired { get; }

    public TimeSpan TimeWaitedForLock { get; }

    public CancellationToken LostToken => CanObserveLoss ? LostTokenSignal : CancellationToken.None;

    public bool CanObserveLoss => Volatile.Read(ref _canObserveLoss) != 0;

    private CancellationToken LostTokenSignal { get; }

    IReadOnlyList<IDistributedLease> ICompositeDistributedLease.Children => _children;

    /// <summary>
    /// Renews every child concurrently. Returns <see langword="true"/> when all children renewed, and
    /// <see langword="false"/> only when this composite was already released. Losing a child throws
    /// <see cref="LockHandleLostException"/> rather than returning <see langword="false"/>: renewals fan out
    /// concurrently, so a surviving sibling has already been extended and is still held, and the composite must
    /// still be released or disposed. Reporting <see langword="false"/> would mean "already lost — nothing to
    /// release" under the <see cref="IDistributedLease"/> contract, which would orphan those survivors.
    /// </summary>
    /// <exception cref="LockHandleLostException">A child lease was lost; its resource and lease id name the child.</exception>
    public async Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_isReleased)
            {
                return false;
            }

            return await CompositeDistributedLeaseOperations
                .RenewAllAsync(
                    _children,
                    child => child.RenewAsync(timeUntilExpires, cancellationToken),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ReleaseAsync()
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await _ReleaseCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Releases (when release-on-dispose is enabled) and disposes every child in reverse order. Idempotent, and
    /// never throws: a cleanup failure is logged through the provider's logger instead, because an exception thrown
    /// from disposal inside <c>await using</c> would replace whatever the caller's body was already throwing. Call
    /// <see cref="ReleaseAsync"/> explicitly when the cleanup outcome must be observed.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= _DisposeCoreAsync();

            return new ValueTask(_disposeTask);
        }
    }

    private async Task _ReleaseCoreAsync()
    {
        if (_isReleased)
        {
            return;
        }

        await CompositeDistributedLeaseOperations
            .RunReverseAsync(_children, static child => child.ReleaseAsync())
            .ConfigureAwait(false);

        _isReleased = true;
    }

    private async Task _DisposeCoreAsync()
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            List<Exception>? errors = null;

            if (_releaseOnDispose && !_isReleased)
            {
                errors = await CompositeDistributedLeaseOperations
                    .CollectReverseAsync(_children, static child => child.ReleaseAsync())
                    .ConfigureAwait(false);

                if (errors is null)
                {
                    _isReleased = true;
                }
            }

            var disposeErrors = await CompositeDistributedLeaseOperations
                .CollectReverseAsync(_children, static child => child.DisposeAsync().AsTask())
                .ConfigureAwait(false);

            if (disposeErrors is not null)
            {
                (errors ??= []).AddRange(disposeErrors);
            }

            // Disposal never throws, matching every other IDistributedLease (DistributedLockHandleBase.DisposeAsync
            // logs and swallows for the same reason). `await using` lowers to try/finally, and an exception from a
            // finally block REPLACES the one already in flight — so throwing here would silently destroy the caller's
            // real exception whenever a release happened to fail. Callers who need the cleanup outcome call
            // ReleaseAsync() explicitly, which still throws LockCleanupFailedException.
            if (errors is not null)
            {
                _logger.LogCompositeLeaseCleanupFailed(new LockCleanupFailedException(errors), Resource, LeaseId);
            }
        }
        finally
        {
            // Child disposal stops their monitors, so the composite must stop advertising a live loss signal.
            Volatile.Write(ref _canObserveLoss, 0);
            _lostSource?.Dispose();
            _lifecycleGate.Release();
        }
    }
}

internal static class CompositeDistributedLeaseOperations
{
    internal static async Task<bool> RenewAllAsync(
        IReadOnlyList<IDistributedLease> children,
        Func<IDistributedLease, Task<bool>> renew,
        CancellationToken cancellationToken = default
    )
    {
        var outcomes = await CollectRenewalOutcomesAsync(children, renew).ConfigureAwait(false);
        ThrowRenewalErrors(outcomes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // A partial renewal cannot be reported as `false`. Renewals fan out concurrently, so by the time one child
        // reports loss its siblings have already been extended by a full lease duration and remain held. The
        // IDistributedLease contract defines `false` as "already lost — nothing to release", and a caller acting on
        // that would abandon the handle and orphan every surviving child until its TTL expires. Throwing names the
        // lost child and cannot be silently ignored; the composite stays held for its survivors, so the caller must
        // still release or dispose it. This matches what the coordinator already does for the identical condition
        // during formation (CompositeDistributedLockAcquireCoordinator._RenewHeldAsync).
        var lostChild = outcomes.FirstOrDefault(static outcome => !outcome.Renewed).Child;

        if (lostChild is not null)
        {
            throw new LockHandleLostException(lostChild.Resource, lostChild.LeaseId);
        }

        return true;
    }

    internal static Task<ChildRenewalOutcome[]> CollectRenewalOutcomesAsync(
        IReadOnlyList<IDistributedLease> children,
        Func<IDistributedLease, Task<bool>> renew
    )
    {
        return Task.WhenAll(children.Select(child => _InvokeRenewAsync(child, renew)));
    }

    internal static void ThrowRenewalErrors(
        IReadOnlyList<ChildRenewalOutcome> outcomes,
        CancellationToken cancellationToken
    )
    {
        var errors = outcomes
            .Where(static outcome => outcome.Exception is not null)
            .Select(static outcome => outcome.Exception!)
            .ToList();

        if (errors.Count == 0)
        {
            return;
        }

        if (
            cancellationToken.IsCancellationRequested && errors.All(static error => error is OperationCanceledException)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        ThrowIfAny(errors);
    }

    internal static async Task RunReverseAsync(
        IReadOnlyList<IDistributedLease> children,
        Func<IDistributedLease, Task> action
    )
    {
        var errors = await CollectReverseAsync(children, action).ConfigureAwait(false);
        ThrowCleanupErrors(errors);
    }

    /// <summary>
    /// Surfaces failures reported while releasing or disposing children. Distinct from <see cref="ThrowIfAny"/>:
    /// cleanup failures carry the dedicated <see cref="LockCleanupFailedException"/> so a caller's
    /// <c>catch (DistributedLockException)</c> — the hierarchy the package documents as its catch-all — sees them.
    /// A raw storage exception or an <see cref="AggregateException"/> would escape that catch entirely.
    /// </summary>
    internal static void ThrowCleanupErrors(List<Exception>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return;
        }

        throw new LockCleanupFailedException(errors);
    }

    internal static async Task<List<Exception>?> CollectReverseAsync(
        IReadOnlyList<IDistributedLease> children,
        Func<IDistributedLease, Task> action
    )
    {
        List<Exception>? errors = null;

        for (var i = children.Count - 1; i >= 0; i--)
        {
            try
            {
                await action(children[i]).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        return errors;
    }

    internal static void ThrowIfAny(List<Exception>? errors)
    {
        if (errors is null)
        {
            return;
        }

        if (errors.Count == 1)
        {
            throw errors[0].ReThrow();
        }

        throw new AggregateException(errors);
    }

    private static async Task<ChildRenewalOutcome> _InvokeRenewAsync(
        IDistributedLease child,
        Func<IDistributedLease, Task<bool>> renew
    )
    {
        try
        {
            return new ChildRenewalOutcome(child, await renew(child).ConfigureAwait(false), Exception: null);
        }
        catch (Exception exception)
        {
            return new ChildRenewalOutcome(child, Renewed: false, exception);
        }
    }

    internal readonly record struct ChildRenewalOutcome(IDistributedLease Child, bool Renewed, Exception? Exception);
}

internal static partial class CompositeDistributedLeaseLogs
{
    /// <summary>
    /// Logs cleanup failures observed while disposing a composite lease (Error). Disposal cannot throw — see
    /// <see cref="CompositeDistributedLease.DisposeAsync"/> — so this is the only signal that one or more of the
    /// set's resources may still be held until their TTL expires.
    /// </summary>
    [LoggerMessage(
        EventId = 1,
        EventName = "CompositeLeaseCleanupFailed",
        Level = LogLevel.Error,
        Message = "Unable to release or dispose every child of composite lock: R={Resource} Id={LeaseId}"
    )]
    public static partial void LogCompositeLeaseCleanupFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );
}
