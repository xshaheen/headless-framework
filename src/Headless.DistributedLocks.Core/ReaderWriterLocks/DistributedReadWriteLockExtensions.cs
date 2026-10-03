// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Composite acquisition over <see cref="IDistributedReadWriteLock"/>: acquires a mixed set of read and write locks
/// all-or-nothing under one acquire budget, in a canonical order that prevents two composites over overlapping
/// resource names from deadlocking against each other.
/// </summary>
[PublicAPI]
public static class DistributedReadWriteLockExtensions
{
    extension(IDistributedReadWriteLock provider)
    {
        /// <summary>
        /// Tries to acquire every distinct resource in <paramref name="requests"/>, in ordinal resource order, under
        /// one acquire budget.
        /// </summary>
        /// <param name="requests">
        /// The resources to acquire and the mode wanted for each. The sequence is enumerated once. A resource
        /// requested as both <see cref="DistributedLockMode.Read"/> and <see cref="DistributedLockMode.Write"/>
        /// collapses to a single <see cref="DistributedLockMode.Write"/> acquisition, because a write lock subsumes a
        /// read lock; identical duplicates are deduplicated.
        /// </param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">
        /// Cancels composite formation. Pending child work is cancelled and drained, and compensating cleanup completes
        /// before cancellation surfaces.
        /// </param>
        /// <returns>
        /// A lease representing the complete canonical set, or <see langword="null"/> when the set cannot be formed
        /// before the acquire timeout. A set canonicalizing to one entry returns the provider's original lease.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Pass the whole set — reads and writes together — in one call. Nesting a read composite inside a write
        /// composite reintroduces exactly the circular wait this method exists to prevent, because neither call ever
        /// sees the complete set and so neither can order it.
        /// </para>
        /// <para>
        /// The returned composite lease is synthetic. Its <see cref="IDistributedLease.Resource"/> is a diagnostic
        /// identity that encodes each entry's mode (for example <c>"r:a+w:b"</c>) and its
        /// <see cref="IDistributedLease.LeaseId"/> is generated locally — neither was ever written to a backend. Never
        /// pass them to a by-resource provider API; release and renew the composite through the handle itself.
        /// </para>
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
        /// canonical entry at a time. A violating name therefore surfaces only when its turn arrives, after earlier
        /// resources in ordinal order have genuinely been acquired (and are then rolled back). No lock is leaked, but
        /// the failure is not fail-fast.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="requests"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="requests"/> is empty, contains a <see langword="null"/> entry, or contains an entry whose
        /// <see cref="DistributedReadWriteLockRequest.Resource"/> is null, empty, or whitespace or whose
        /// <see cref="DistributedReadWriteLockRequest.Mode"/> is <see cref="DistributedLockMode.None"/> or undefined;
        /// or <see cref="DistributedLockAcquireOptions.Monitoring"/> is <see cref="LockMonitoringMode.Monitor"/> or
        /// <see cref="LockMonitoringMode.AutoExtend"/> but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/>
        /// is <see cref="Timeout.InfiniteTimeSpan"/> (monitoring requires a finite lease).
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
        /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large; or a resource exceeds the provider's maximum
        /// resource-name length — the latter raised by the provider mid-acquisition, not up front (see remarks).
        /// </exception>
        /// <exception cref="LockHandleLostException">A held child lease was lost while the complete set was forming.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="AggregateException">
        /// A child release or disposal failed during compensating rollback. The primary failure (cancellation, timeout,
        /// or the fault that triggered rollback) is first, followed by every cleanup failure. When rollback reports a
        /// failure and there is no primary, <see cref="LockCleanupFailedException"/> is thrown instead.
        /// </exception>
        public async Task<IDistributedLease?> TryAcquireAllAsync(
            IEnumerable<DistributedReadWriteLockRequest> requests,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var result = await _TryAcquireAllCoreAsync(provider, requests, options, cancellationToken)
                .ConfigureAwait(false);

            return result.Lease;
        }

        /// <summary>
        /// Acquires every distinct resource in <paramref name="requests"/>, in ordinal resource order, under one
        /// acquire budget.
        /// </summary>
        /// <param name="requests">
        /// The resources to acquire and the mode wanted for each. The sequence is enumerated once. A resource
        /// requested as both <see cref="DistributedLockMode.Read"/> and <see cref="DistributedLockMode.Write"/>
        /// collapses to a single <see cref="DistributedLockMode.Write"/> acquisition; identical duplicates are
        /// deduplicated.
        /// </param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">
        /// Cancels composite formation. Pending child work is cancelled and drained, and compensating cleanup completes
        /// before cancellation surfaces.
        /// </param>
        /// <returns>
        /// A lease representing the complete canonical set. A set canonicalizing to one entry returns the provider's
        /// original lease.
        /// </returns>
        /// <remarks>
        /// <inheritdoc cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[1]"/>
        /// <inheritdoc cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[2]"/>
        /// <para>
        /// Compensating rollback releases and disposes every child already acquired, and a failure reported by one of
        /// those cleanup calls is surfaced rather than hidden — so a failed acquisition can throw an
        /// <see cref="AggregateException"/> (or <see cref="LockCleanupFailedException"/>) instead of
        /// <see cref="LockAcquisitionTimeoutException"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="requests"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="requests"/> is empty, contains a <see langword="null"/> entry, or contains an entry whose
        /// <see cref="DistributedReadWriteLockRequest.Resource"/> is null, empty, or whitespace or whose
        /// <see cref="DistributedReadWriteLockRequest.Mode"/> is <see cref="DistributedLockMode.None"/> or undefined;
        /// or monitoring is requested while <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
        /// <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
        /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large; or a resource exceeds the provider's maximum
        /// resource-name length, raised by the provider mid-acquisition.
        /// </exception>
        /// <exception cref="LockHandleLostException">A held child lease was lost while the complete set was forming.</exception>
        /// <exception cref="LockAcquisitionTimeoutException">
        /// The complete set could not be acquired before the timeout elapsed.
        /// <see cref="LockAcquisitionTimeoutException.Resource"/> carries the joined canonical set (for example
        /// <c>"r:a+w:b"</c>), not the single resource that blocked, and is diagnostic only — it is not a storage key
        /// and must not be split back into resource names.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="AggregateException">
        /// A child release or disposal failed during compensating rollback. The primary failure (cancellation, timeout,
        /// or the fault that triggered rollback) is first, followed by every cleanup failure. When rollback reports a
        /// failure and there is no primary, <see cref="LockCleanupFailedException"/> is thrown instead.
        /// </exception>
        public async Task<IDistributedLease> AcquireAllAsync(
            IEnumerable<DistributedReadWriteLockRequest> requests,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var result = await _TryAcquireAllCoreAsync(provider, requests, options, cancellationToken)
                .ConfigureAwait(false);

            return result.LeaseOrThrow();
        }

        /// <summary>
        /// Tries to acquire a read (shared) lock on every distinct name in <paramref name="resources"/>, in ordinal
        /// order, under one acquire budget. Sugar over
        /// <see cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)"/>
        /// for the uniform read-set case.
        /// </summary>
        /// <param name="resources">Resource names to read-lock. The sequence is enumerated once and deduplicated.</param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">Cancels composite formation; compensating cleanup completes first.</param>
        /// <returns>
        /// A lease representing the complete canonical set, or <see langword="null"/> when it cannot be formed before
        /// the acquire timeout. A set of one distinct resource returns the provider's original lease.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Use the mixed
        /// <see cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)"/>
        /// overload when the caller also needs a write lock. Taking a read set and a write set as two separate
        /// composites can deadlock — neither call sees the complete set, so neither can order it.
        /// </para>
        /// <inheritdoc cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[2]"/>
        /// </remarks>
        public Task<IDistributedLease?> TryAcquireAllReadAsync(
            IEnumerable<string> resources,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.TryAcquireAllAsync(
                _ToUniformRequests(resources, DistributedLockMode.Read),
                options,
                cancellationToken
            );
        }

        /// <summary>
        /// Acquires a read (shared) lock on every distinct name in <paramref name="resources"/>, in ordinal order,
        /// under one acquire budget. Sugar over
        /// <see cref="AcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)"/>
        /// for the uniform read-set case.
        /// </summary>
        /// <param name="resources">Resource names to read-lock. The sequence is enumerated once and deduplicated.</param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">Cancels composite formation; compensating cleanup completes first.</param>
        /// <returns>A lease representing the complete canonical set. A set of one distinct resource returns the provider's original lease.</returns>
        /// <remarks>
        /// <inheritdoc cref="TryAcquireAllReadAsync(IDistributedReadWriteLock,IEnumerable{string},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[1]"/>
        /// <inheritdoc cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[2]"/>
        /// </remarks>
        /// <exception cref="LockAcquisitionTimeoutException">The complete set could not be acquired before the timeout elapsed.</exception>
        public Task<IDistributedLease> AcquireAllReadAsync(
            IEnumerable<string> resources,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.AcquireAllAsync(
                _ToUniformRequests(resources, DistributedLockMode.Read),
                options,
                cancellationToken
            );
        }

        /// <summary>
        /// Tries to acquire a write (exclusive) lock on every distinct name in <paramref name="resources"/>, in
        /// ordinal order, under one acquire budget. Sugar over
        /// <see cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)"/>
        /// for the uniform write-set case.
        /// </summary>
        /// <param name="resources">Resource names to write-lock. The sequence is enumerated once and deduplicated.</param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">Cancels composite formation; compensating cleanup completes first.</param>
        /// <returns>
        /// A lease representing the complete canonical set, or <see langword="null"/> when it cannot be formed before
        /// the acquire timeout. A set of one distinct resource returns the provider's original lease.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Use the mixed
        /// <see cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)"/>
        /// overload when the caller also needs a read lock. Taking a read set and a write set as two separate
        /// composites can deadlock — neither call sees the complete set, so neither can order it.
        /// </para>
        /// <inheritdoc cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[2]"/>
        /// </remarks>
        public Task<IDistributedLease?> TryAcquireAllWriteAsync(
            IEnumerable<string> resources,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.TryAcquireAllAsync(
                _ToUniformRequests(resources, DistributedLockMode.Write),
                options,
                cancellationToken
            );
        }

        /// <summary>
        /// Acquires a write (exclusive) lock on every distinct name in <paramref name="resources"/>, in ordinal order,
        /// under one acquire budget. Sugar over
        /// <see cref="AcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)"/>
        /// for the uniform write-set case.
        /// </summary>
        /// <param name="resources">Resource names to write-lock. The sequence is enumerated once and deduplicated.</param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">Cancels composite formation; compensating cleanup completes first.</param>
        /// <returns>A lease representing the complete canonical set. A set of one distinct resource returns the provider's original lease.</returns>
        /// <remarks>
        /// <inheritdoc cref="TryAcquireAllWriteAsync(IDistributedReadWriteLock,IEnumerable{string},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[1]"/>
        /// <inheritdoc cref="TryAcquireAllAsync(IDistributedReadWriteLock,IEnumerable{DistributedReadWriteLockRequest},DistributedLockAcquireOptions,CancellationToken)" path="/remarks/para[2]"/>
        /// </remarks>
        /// <exception cref="LockAcquisitionTimeoutException">The complete set could not be acquired before the timeout elapsed.</exception>
        public Task<IDistributedLease> AcquireAllWriteAsync(
            IEnumerable<string> resources,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.AcquireAllAsync(
                _ToUniformRequests(resources, DistributedLockMode.Write),
                options,
                cancellationToken
            );
        }
    }

    private static Task<CompositeAcquireResult> _TryAcquireAllCoreAsync(
        IDistributedReadWriteLock provider,
        IEnumerable<DistributedReadWriteLockRequest> requests,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(provider);
        Argument.IsNotNull(requests);

        var canonicalRequests = _MaterializeCanonicalRequests(requests);

        // A read child cannot hold an infinite lease -- the provider clamps it to DefaultTimeUntilExpires so a crashed
        // reader cannot strand the resource forever. Tell the coordinator, or it would classify this set as
        // non-expiring, never renew it during formation, and hand back a lease whose read children had already expired.
        var environment = CompositeAcquireEnvironment.From(
            provider,
            clampsInfiniteTimeUntilExpires: canonicalRequests.Any(static request =>
                request.Mode is DistributedLockMode.Read
            )
        );

        return CompositeAcquireCoordinator.TryAcquireAsync(
            canonicalRequests,
            static request => request.Resource,
            (request, childOptions, childToken) =>
                request.Mode is DistributedLockMode.Write
                    ? provider.TryAcquireWriteLockAsync(request.Resource, childOptions, childToken)
                    : provider.TryAcquireReadLockAsync(request.Resource, childOptions, childToken),
            _GetCompositeResource,
            environment,
            options,
            cancellationToken
        );
    }

    /// <summary>
    /// Validates every request, collapses each resource to a single mode, and ordinal-sorts the set <em>by resource</em>.
    /// </summary>
    /// <remarks>
    /// Ordering by resource — not by a composed <c>"a:read"</c>-style key — is what keeps this composite orderable
    /// against every other composite over the same names. Sorting a composed key would place <c>"a:write"</c> after
    /// <c>"a:x:read"</c> for the resources <c>a</c> and <c>a:x</c>, breaking the single global resource order that
    /// prevents circular wait. Ordering by resource alone is total precisely because the write-subsumes-read collapse
    /// leaves each resource in the set exactly once.
    /// </remarks>
    private static DistributedReadWriteLockRequest[] _MaterializeCanonicalRequests(
        IEnumerable<DistributedReadWriteLockRequest> requests
    )
    {
        var materialized = new List<DistributedReadWriteLockRequest>();

        foreach (var request in requests)
        {
            Argument.IsNotNull(request, paramName: nameof(requests));
            Argument.IsNotNullOrWhiteSpace(request.Resource, paramName: nameof(requests));
            Argument.IsNotDefault(request.Mode, paramName: nameof(requests));
            Argument.IsInEnum(request.Mode, paramName: nameof(requests));
            materialized.Add(request);
        }

        Argument.IsNotEmpty(materialized, paramName: nameof(requests));

        var collapsed = new Dictionary<string, DistributedLockMode>(StringComparer.Ordinal);

        foreach (var request in materialized)
        {
            var alreadyWrite =
                collapsed.TryGetValue(request.Resource, out var existing) && existing is DistributedLockMode.Write;

            collapsed[request.Resource] = alreadyWrite ? DistributedLockMode.Write : request.Mode;
        }

        return
        [
            .. collapsed
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => new DistributedReadWriteLockRequest(entry.Key, entry.Value)),
        ];
    }

    /// <summary>
    /// Builds the composite's diagnostic identity, encoding each entry's mode (<c>"r:a+w:b"</c>) so a read set and a
    /// write set over the same resources are distinguishable. This name exists in no backend — never pass it to a
    /// by-resource provider API.
    /// </summary>
    private static string _GetCompositeResource(IReadOnlyList<DistributedReadWriteLockRequest> canonicalRequests)
    {
        return string.Join(
            '+',
            canonicalRequests.Select(static request =>
                (request.Mode is DistributedLockMode.Write ? "w:" : "r:") + request.Resource
            )
        );
    }

    private static IEnumerable<DistributedReadWriteLockRequest> _ToUniformRequests(
        IEnumerable<string> resources,
        DistributedLockMode mode
    )
    {
        Argument.IsNotNull(resources);

        return resources.Select(resource => new DistributedReadWriteLockRequest(resource, mode));
    }
}
