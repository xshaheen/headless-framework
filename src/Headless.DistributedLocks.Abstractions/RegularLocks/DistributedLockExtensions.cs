// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Convenience extensions over <see cref="IDistributedLock"/> for releasing/renewing a held
/// <see cref="IDistributedLease"/> and for the acquire-run-release ("try-using") pattern that owns the
/// handle lifetime so callers do not have to manage <c>await using</c> themselves.
/// </summary>
[PublicAPI]
public static class DistributedLockExtensions
{
    extension(IDistributedLock provider)
    {
        /// <summary>
        /// Tries to acquire every distinct <paramref name="resources"/> in ordinal order under one acquire budget.
        /// </summary>
        /// <param name="resources">Resource names to acquire. The sequence is enumerated once, deduplicated, and ordinal-sorted.</param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">
        /// Cancels composite formation. Pending child work is cancelled and drained, and compensating cleanup completes
        /// before cancellation surfaces.
        /// </param>
        /// <returns>
        /// A lease representing the complete canonical set, or <see langword="null"/> when the set cannot be formed
        /// before the acquire timeout. A set containing one distinct resource returns the provider's original lease.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Resource identity is defined by <see cref="StringComparer.Ordinal"/> before the provider is called. A custom
        /// provider whose backend aliases ordinal-distinct names must reject non-canonical names or require callers to
        /// canonicalize them before invoking this method; provider-side normalization is too late and can make the
        /// composite contend with itself.
        /// </para>
        /// <para>
        /// A failed acquisition does not always return <see langword="null"/>. Compensating rollback releases and
        /// disposes every child already acquired, and a failure reported by one of those cleanup calls is surfaced
        /// rather than hidden: the call then throws instead of returning <see langword="null"/>. Callers that branch
        /// only on a <see langword="null"/> result and <see cref="OperationCanceledException"/> will miss that case.
        /// </para>
        /// <para>
        /// Only the whole-call arguments are validated eagerly. Per-resource constraints owned by the provider — the
        /// maximum resource-name length in particular — are enforced by the provider on its own acquire call, one
        /// canonical resource at a time. A violating name therefore surfaces only when its turn arrives, after earlier
        /// resources in ordinal order have genuinely been acquired (and are then rolled back). No lock is leaked, but
        /// the failure is not fail-fast.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="resources"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="resources"/> is empty or contains a null, empty, or whitespace resource name; or
        /// <see cref="DistributedLockAcquireOptions.Monitoring"/> is <see cref="LockMonitoringMode.Monitor"/> or
        /// <see cref="LockMonitoringMode.AutoExtend"/> but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/>
        /// is <see cref="Timeout.InfiniteTimeSpan"/> (monitoring requires a finite lease).
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
        /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large; or a value in <paramref name="resources"/> exceeds the
        /// provider's maximum resource-name length — the latter raised by the provider mid-acquisition, not up front
        /// (see remarks).
        /// </exception>
        /// <exception cref="LockHandleLostException">A held child lease was lost while the complete set was forming.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="AggregateException">
        /// A child release or disposal failed during compensating rollback. The primary failure (cancellation, timeout,
        /// or the fault that triggered rollback) is first, followed by every cleanup failure. When rollback reports a
        /// single failure and there is no primary, that exception is rethrown as-is instead.
        /// </exception>
        public async Task<IDistributedLease?> TryAcquireAllAsync(
            IEnumerable<string> resources,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var result = await CompositeDistributedLockAcquireCoordinator
                .TryAcquireAsync(provider, resources, options, cancellationToken)
                .ConfigureAwait(false);

            return result.Lease;
        }

        /// <summary>
        /// Acquires every distinct <paramref name="resources"/> in ordinal order under one acquire budget.
        /// </summary>
        /// <param name="resources">Resource names to acquire. The sequence is enumerated once, deduplicated, and ordinal-sorted.</param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">
        /// Cancels composite formation. Pending child work is cancelled and drained, and compensating cleanup completes
        /// before cancellation surfaces.
        /// </param>
        /// <returns>
        /// A lease representing the complete canonical set. A set containing one distinct resource returns the
        /// provider's original lease.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Resource identity is defined by <see cref="StringComparer.Ordinal"/> before the provider is called. A custom
        /// provider whose backend aliases ordinal-distinct names must reject non-canonical names or require callers to
        /// canonicalize them before invoking this method; provider-side normalization is too late and can make the
        /// composite contend with itself.
        /// </para>
        /// <para>
        /// Compensating rollback releases and disposes every child already acquired, and a failure reported by one of
        /// those cleanup calls is surfaced rather than hidden — so a failed acquisition can throw an
        /// <see cref="AggregateException"/> (or the child's own exception) instead of
        /// <see cref="LockAcquisitionTimeoutException"/>.
        /// </para>
        /// <para>
        /// Only the whole-call arguments are validated eagerly. Per-resource constraints owned by the provider — the
        /// maximum resource-name length in particular — are enforced by the provider on its own acquire call, one
        /// canonical resource at a time. A violating name therefore surfaces only when its turn arrives, after earlier
        /// resources in ordinal order have genuinely been acquired (and are then rolled back). No lock is leaked, but
        /// the failure is not fail-fast.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="resources"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="resources"/> is empty or contains a null, empty, or whitespace resource name; or
        /// <see cref="DistributedLockAcquireOptions.Monitoring"/> is <see cref="LockMonitoringMode.Monitor"/> or
        /// <see cref="LockMonitoringMode.AutoExtend"/> but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/>
        /// is <see cref="Timeout.InfiniteTimeSpan"/> (monitoring requires a finite lease).
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
        /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large; or a value in <paramref name="resources"/> exceeds the
        /// provider's maximum resource-name length — the latter raised by the provider mid-acquisition, not up front
        /// (see remarks).
        /// </exception>
        /// <exception cref="LockHandleLostException">A held child lease was lost while the complete set was forming.</exception>
        /// <exception cref="LockAcquisitionTimeoutException">
        /// The complete set could not be acquired before the timeout elapsed.
        /// <see cref="LockAcquisitionTimeoutException.Resource"/> carries the joined canonical set (for example
        /// <c>"a+b"</c>), not the single resource that blocked, and is diagnostic only — it is not a storage key and
        /// must not be split back into resource names.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="AggregateException">
        /// A child release or disposal failed during compensating rollback. The primary failure (cancellation, timeout,
        /// or the fault that triggered rollback) is first, followed by every cleanup failure. When rollback reports a
        /// single failure and there is no primary, that exception is rethrown as-is instead.
        /// </exception>
        public async Task<IDistributedLease> AcquireAllAsync(
            IEnumerable<string> resources,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var result = await CompositeDistributedLockAcquireCoordinator
                .TryAcquireAsync(provider, resources, options, cancellationToken)
                .ConfigureAwait(false);

            return result.Lease
                ?? throw (
                    result.TryOnce
                        ? LockAcquisitionTimeoutException.ForTryOnceContention(result.Resource)
                        : new LockAcquisitionTimeoutException(result.Resource)
                );
        }

        /// <summary>Releases the resource lock held by <paramref name="distributedLock"/>.</summary>
        /// <param name="distributedLock">The held lease to release; supplies the resource and lease id.</param>
        /// <param name="cancellationToken">Cancels the release; surfaces as <see cref="OperationCanceledException"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
        public Task ReleaseAsync(IDistributedLease distributedLock, CancellationToken cancellationToken = default)
        {
            Argument.IsNotNull(provider);

            if (distributedLock is ICompositeDistributedLease composite)
            {
                return _ReleaseCompositeAsync(provider, composite, cancellationToken);
            }

            return provider.ReleaseAsync(distributedLock.Resource, distributedLock.LeaseId, cancellationToken);
        }

        /// <summary>
        /// Renews a resource lock for a specified <paramref name="distributedLock"/> by extending
        /// the expiration time of the lock if it is still held to the <see cref="IDistributedLease.LeaseId"/>
        /// and return <see langword="true"/>, otherwise <see langword="false"/>.
        /// </summary>
        public Task<bool> RenewAsync(
            IDistributedLease distributedLock,
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        )
        {
            if (distributedLock is ICompositeDistributedLease composite)
            {
                return CompositeDistributedLeaseOperations.RenewAllAsync(
                    composite.Children,
                    child => provider.RenewAsync(child.Resource, child.LeaseId, timeUntilExpires, cancellationToken),
                    cancellationToken
                );
            }

            return provider.RenewAsync(
                distributedLock.Resource,
                distributedLock.LeaseId,
                timeUntilExpires,
                cancellationToken
            );
        }

        /// <summary>
        /// Tries to acquire a lock for a specified <paramref name="resource"/> and execute the <paramref name="work"/>.
        /// </summary>
        /// <param name="resource">The resource to acquire the lock for.</param>
        /// <param name="work">The async work to execute while holding the lock.</param>
        /// <param name="options">
        /// Per-call configuration. See <see cref="DistributedLockAcquireOptions"/>.
        /// The extension owns the handle lifetime, so <see cref="DistributedLockAcquireOptions.ReleaseOnDispose"/>
        /// is forced to <see langword="true"/> regardless of what the caller sets.
        /// Overloads taking a <see cref="CancellationToken"/>-receiving <paramref name="work"/>
        /// delegate forward a token that is also cancelled when the lease is lost (when monitoring is enabled).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            // `await using` ensures DisposeAsync runs on exit — DisposeAsync drains the lease
            // monitor (cancelling its CTS) and then releases the storage row. ReleaseAsync alone
            // would leave the monitor's internal CTS un-cancelled until GC.
            await work().ConfigureAwait(false);

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLock,string,Func{Task},DistributedLockAcquireOptions,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            await work(state).ConfigureAwait(false);

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLock,string,Func{Task},DistributedLockAcquireOptions,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<CancellationToken, Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            // When monitoring is enabled, link the caller's CT with the lease-lost token so
            // the work delegate observes lease loss promptly via its CancellationToken.
            if (distributedLock.CanObserveLoss)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    distributedLock.LostToken
                );

                await work(linkedCts.Token).ConfigureAwait(false);
            }
            else
            {
                await work(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLock,string,Func{Task},DistributedLockAcquireOptions,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, CancellationToken, Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            if (distributedLock.CanObserveLoss)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    distributedLock.LostToken
                );

                await work(state, linkedCts.Token).ConfigureAwait(false);
            }
            else
            {
                await work(state, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Tries to acquire a lock for <paramref name="resource"/> and runs a synchronous
        /// <paramref name="work"/> delegate. Synchronous work cannot observe a lease-lost
        /// cancellation, so this overload always passes <see cref="LockMonitoringMode.None"/>.
        /// Use the <see cref="Func{Task}"/>-receiving overload when you want lease monitoring.
        /// </summary>
        public async Task<bool> TryUsingAsync(
            string resource,
            Action work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var options = new DistributedLockAcquireOptions
            {
                TimeUntilExpires = timeUntilExpires,
                AcquireTimeout = acquireTimeout,
            };

            return await provider.TryUsingAsync(resource, work, options, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLock,string,Action,TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Action work,
            DistributedLockAcquireOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with
            {
                ReleaseOnDispose = true,
                Monitoring = LockMonitoringMode.None,
            };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            work();

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLock,string,Action,TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Action<TState> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var options = new DistributedLockAcquireOptions
            {
                TimeUntilExpires = timeUntilExpires,
                AcquireTimeout = acquireTimeout,
            };

            return await provider
                .TryUsingAsync(resource, state, work, options, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc cref="TryUsingAsync{TState}(IDistributedLock,string,TState,Action{TState},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Action<TState> work,
            DistributedLockAcquireOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with
            {
                ReleaseOnDispose = true,
                Monitoring = LockMonitoringMode.None,
            };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            work(state);

            return true;
        }
    }

    private static async Task _ReleaseCompositeAsync(
        IDistributedLock provider,
        ICompositeDistributedLease composite,
        CancellationToken cancellationToken
    )
    {
        var errors = await CompositeDistributedLeaseOperations
            .CollectReverseAsync(
                composite.Children,
                child => provider.ReleaseAsync(child.Resource, child.LeaseId, cancellationToken)
            )
            .ConfigureAwait(false);

        if (
            cancellationToken.IsCancellationRequested
            && errors is not null
            && errors.All(static error => error is OperationCanceledException)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        CompositeDistributedLeaseOperations.ThrowCleanupErrors(errors);
    }
}
