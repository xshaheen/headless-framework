// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;

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
    private readonly CancellationTokenSource? _lostSource;
    // The contract deliberately permits renew/release after DisposeAsync when release-on-dispose is false,
    // so disposing this gate as part of DisposeAsync would introduce a lifecycle race.
#pragma warning disable CA2213
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
#pragma warning restore CA2213
    private readonly Lock _disposeLock = new();

    private Task? _disposeTask;
    private bool _isReleased;

    internal CompositeDistributedLease(
        IReadOnlyList<IDistributedLease> children,
        DateTimeOffset dateAcquired,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose
    )
    {
        Argument.IsNotNull(children);
        Argument.IsGreaterThanOrEqualTo(children.Count, 2);

        _children = [.. children];
        _releaseOnDispose = releaseOnDispose;

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
        }

        LeaseId = Guid.NewGuid().ToString("N");
        Resource = string.Join("+", _children.Select(child => child.Resource));
        DateAcquired = dateAcquired;
        TimeWaitedForLock = timeWaitedForLock;
        CanObserveLoss = canObserveLoss;
        LostToken = _lostSource?.Token ?? CancellationToken.None;
    }

    public string LeaseId { get; }

    public long? FencingToken => null;

    public string Resource { get; }

    public int RenewalCount => _children.Min(child => child.RenewalCount);

    public DateTimeOffset DateAcquired { get; }

    public TimeSpan TimeWaitedForLock { get; }

    public CancellationToken LostToken { get; }

    public bool CanObserveLoss { get; }

    IReadOnlyList<IDistributedLease> ICompositeDistributedLease.Children => _children;

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

            CompositeDistributedLeaseOperations.ThrowIfAny(errors);
        }
        finally
        {
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
        var tasks = children.Select(child => _InvokeRenewAsync(child, renew)).ToArray();
        var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);

        List<Exception>? errors = null;
        var renewed = true;

        foreach (var outcome in outcomes)
        {
            if (outcome.Exception is { } exception)
            {
                (errors ??= []).Add(exception);
                continue;
            }

            renewed &= outcome.Renewed;
        }

        if (errors is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return renewed;
        }

        if (
            cancellationToken.IsCancellationRequested && errors.All(static error => error is OperationCanceledException)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        ThrowIfAny(errors);

        return renewed;
    }

    internal static async Task RunReverseAsync(
        IReadOnlyList<IDistributedLease> children,
        Func<IDistributedLease, Task> action
    )
    {
        var errors = await CollectReverseAsync(children, action).ConfigureAwait(false);
        ThrowIfAny(errors);
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
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        throw new AggregateException(errors);
    }

    private static async Task<RenewOutcome> _InvokeRenewAsync(
        IDistributedLease child,
        Func<IDistributedLease, Task<bool>> renew
    )
    {
        try
        {
            return new RenewOutcome(await renew(child).ConfigureAwait(false), Exception: null);
        }
        catch (Exception exception)
        {
            return new RenewOutcome(Renewed: false, exception);
        }
    }

    private readonly record struct RenewOutcome(bool Renewed, Exception? Exception);
}
