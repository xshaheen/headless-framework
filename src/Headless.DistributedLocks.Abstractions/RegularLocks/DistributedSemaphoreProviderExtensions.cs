// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Composite acquisition over <see cref="IDistributedSemaphoreProvider"/>: takes one slot of every requested
/// semaphore, all-or-nothing, under a single acquire budget.
/// </summary>
/// <remarks>
/// This hangs off the <em>provider</em>, not off <see cref="IDistributedSemaphore"/>, because a semaphore binds its
/// resource and capacity at construction and its <see cref="IDistributedSemaphore.TryAcquireAsync"/> takes no resource
/// argument — a single semaphore instance cannot compose. The provider's
/// <see cref="IDistributedSemaphoreProvider.CreateSemaphore"/> is the only way to materialize the children, which is
/// why each request carries its own <see cref="DistributedSemaphoreRequest.MaxCount"/>.
/// </remarks>
[PublicAPI]
public static class DistributedSemaphoreProviderExtensions
{
    extension(IDistributedSemaphoreProvider provider)
    {
        /// <summary>
        /// Tries to acquire one slot of every distinct semaphore in <paramref name="requests"/>, in ordinal resource
        /// order, under one acquire budget.
        /// </summary>
        /// <param name="requests">
        /// The semaphores to acquire a slot of. The sequence is enumerated once, deduplicated by resource, and
        /// ordinal-sorted. Each request supplies the capacity its semaphore is created with.
        /// </param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">
        /// Cancels composite formation. Pending child work is cancelled and drained, and compensating cleanup completes
        /// before cancellation surfaces.
        /// </param>
        /// <returns>
        /// A lease representing the complete canonical set, or <see langword="null"/> when the set cannot be formed
        /// before the acquire timeout. A set naming one distinct resource returns the semaphore's original slot lease,
        /// preserving its real <see cref="IDistributedLease.LeaseId"/> and <see cref="IDistributedLease.FencingToken"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The returned composite is a diagnostic wrapper, not a backend object: its
        /// <see cref="IDistributedLease.Resource"/> (the joined canonical set, for example <c>"a+b"</c>) and its
        /// synthetic <see cref="IDistributedLease.LeaseId"/> exist in <em>no</em> backend. Never pass them to a
        /// by-resource provider API. Its <see cref="IDistributedLease.FencingToken"/> is <see langword="null"/> even
        /// though each individual slot carries one — there is no single token for a set.
        /// </para>
        /// <para>
        /// A composite takes exactly one slot of each named semaphore. Duplicate requests for one resource collapse to
        /// a single child; they do not take two permits. See <see cref="DistributedSemaphoreRequest"/>.
        /// </para>
        /// <para>
        /// Resource identity is defined by <see cref="StringComparer.Ordinal"/> before the provider is called. A
        /// provider whose backend aliases ordinal-distinct names must reject non-canonical names or require callers to
        /// canonicalize them first; normalizing inside <see cref="IDistributedSemaphoreProvider.CreateSemaphore"/> is
        /// too late and can make the composite contend with itself.
        /// </para>
        /// <para>
        /// A failed acquisition does not always return <see langword="null"/>. Compensating rollback releases and
        /// disposes every slot already acquired, and a failure reported by one of those cleanup calls is surfaced
        /// rather than hidden: the call then throws instead of returning <see langword="null"/>. Callers that branch
        /// only on a <see langword="null"/> result and <see cref="OperationCanceledException"/> will miss that case.
        /// </para>
        /// <para>
        /// A semaphore slot is stored with a finite expiry score, so
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> of <see cref="Timeout.InfiniteTimeSpan"/> is
        /// rejected by the semaphore regardless of <see cref="DistributedLockAcquireOptions.Monitoring"/>. Held slots
        /// are therefore always renewed at half their TTL (capped at one minute) while later slots are still pending,
        /// unless <see cref="LockMonitoringMode.AutoExtend"/> already owns renewal.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/>, <paramref name="requests"/>, or an element of <paramref name="requests"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="requests"/> is empty; contains a null, empty, or whitespace resource name; names one
        /// resource twice with conflicting <see cref="DistributedSemaphoreRequest.MaxCount"/> values (the message names
        /// the resource); or <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
        /// <see cref="Timeout.InfiniteTimeSpan"/> (a slot requires a finite lease).
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A <see cref="DistributedSemaphoreRequest.MaxCount"/> is less than 1; or
        /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
        /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
        /// </exception>
        /// <exception cref="LockHandleLostException">A held slot was lost while the complete set was forming.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="AggregateException">
        /// A child release or disposal failed during compensating rollback. The primary failure (cancellation, timeout,
        /// or the fault that triggered rollback) is first, followed by every cleanup failure. When rollback reports a
        /// failure and there is no primary, <see cref="LockCleanupFailedException"/> is thrown instead.
        /// </exception>
        public async Task<IDistributedLease?> TryAcquireAllAsync(
            IEnumerable<DistributedSemaphoreRequest> requests,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var result = await _TryAcquireAllAsync(provider, requests, options, cancellationToken)
                .ConfigureAwait(false);

            return result.Lease;
        }

        /// <summary>
        /// Acquires one slot of every distinct semaphore in <paramref name="requests"/>, in ordinal resource order,
        /// under one acquire budget.
        /// </summary>
        /// <param name="requests">
        /// The semaphores to acquire a slot of. The sequence is enumerated once, deduplicated by resource, and
        /// ordinal-sorted. Each request supplies the capacity its semaphore is created with.
        /// </param>
        /// <param name="options">Per-call configuration shared by every child acquisition.</param>
        /// <param name="cancellationToken">
        /// Cancels composite formation. Pending child work is cancelled and drained, and compensating cleanup completes
        /// before cancellation surfaces.
        /// </param>
        /// <returns>
        /// A lease representing the complete canonical set. A set naming one distinct resource returns the semaphore's
        /// original slot lease.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The returned composite is a diagnostic wrapper, not a backend object: its
        /// <see cref="IDistributedLease.Resource"/> (the joined canonical set, for example <c>"a+b"</c>) and its
        /// synthetic <see cref="IDistributedLease.LeaseId"/> exist in <em>no</em> backend. Never pass them to a
        /// by-resource provider API. Its <see cref="IDistributedLease.FencingToken"/> is <see langword="null"/> even
        /// though each individual slot carries one.
        /// </para>
        /// <para>
        /// Compensating rollback releases and disposes every slot already acquired, and a failure reported by one of
        /// those cleanup calls is surfaced rather than hidden — so a failed acquisition can throw an
        /// <see cref="AggregateException"/> (or <see cref="LockCleanupFailedException"/>) instead of
        /// <see cref="LockAcquisitionTimeoutException"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="provider"/>, <paramref name="requests"/>, or an element of <paramref name="requests"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="requests"/> is empty; contains a null, empty, or whitespace resource name; names one
        /// resource twice with conflicting <see cref="DistributedSemaphoreRequest.MaxCount"/> values (the message names
        /// the resource); or <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
        /// <see cref="Timeout.InfiniteTimeSpan"/> (a slot requires a finite lease).
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A <see cref="DistributedSemaphoreRequest.MaxCount"/> is less than 1; or
        /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
        /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
        /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
        /// </exception>
        /// <exception cref="LockHandleLostException">A held slot was lost while the complete set was forming.</exception>
        /// <exception cref="LockAcquisitionTimeoutException">
        /// The complete set could not be acquired before the timeout elapsed.
        /// <see cref="LockAcquisitionTimeoutException.Resource"/> carries the joined canonical set (for example
        /// <c>"a+b"</c>), not the single resource that blocked, and is diagnostic only — it is not a storage key and
        /// must not be split back into resource names.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="AggregateException">
        /// A child release or disposal failed during compensating rollback. The primary failure (cancellation, timeout,
        /// or the fault that triggered rollback) is first, followed by every cleanup failure.
        /// </exception>
        public async Task<IDistributedLease> AcquireAllAsync(
            IEnumerable<DistributedSemaphoreRequest> requests,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var result = await _TryAcquireAllAsync(provider, requests, options, cancellationToken)
                .ConfigureAwait(false);

            return result.Lease
                ?? throw (
                    result.TryOnce
                        ? LockAcquisitionTimeoutException.ForTryOnceContention(result.Resource)
                        : new LockAcquisitionTimeoutException(result.Resource)
                );
        }
    }

    /// <summary>
    /// The semaphore adapter over <see cref="CompositeAcquireCoordinator"/>: canonicalizes the request set, materializes
    /// one <see cref="IDistributedSemaphore"/> per canonical resource, and supplies their
    /// <see cref="IDistributedSemaphore.TryAcquireAsync"/> as the child-acquire delegate. The delegate has to close over
    /// the created semaphore because that method takes no resource argument.
    /// </summary>
    private static Task<CompositeAcquireResult> _TryAcquireAllAsync(
        IDistributedSemaphoreProvider provider,
        IEnumerable<DistributedSemaphoreRequest> requests,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(provider);
        Argument.IsNotNull(requests);

        // Every request is validated here, so no provider call happens on an invalid set.
        var canonicalRequests = _MaterializeCanonicalRequests(requests);
        var semaphores = new Dictionary<string, IDistributedSemaphore>(
            canonicalRequests.Length,
            StringComparer.Ordinal
        );

        foreach (var request in canonicalRequests)
        {
            semaphores.Add(request.Resource, provider.CreateSemaphore(request.Resource, request.MaxCount));
        }

        var environment = new CompositeAcquireEnvironment(
            provider.TimeProvider,
            provider.Logger,
            provider.DefaultAcquireTimeout,
            provider.DefaultTimeUntilExpires
        );

        return CompositeAcquireCoordinator.TryAcquireAsync(
            canonicalRequests,
            static request => request.Resource,
            (request, childOptions, childToken) =>
                semaphores[request.Resource].TryAcquireAsync(childOptions, childToken),
            _GetCompositeResource,
            environment,
            options,
            cancellationToken
        );
    }

    /// <summary>
    /// Validates, deduplicates, and ordinal-sorts the requested semaphores. Ordinal ordering by resource is what
    /// prevents two composites over overlapping names from deadlocking against each other. A duplicate resource with a
    /// conflicting capacity is a caller bug — the two requests describe two different semaphores that cannot both
    /// exist — so it is rejected here, before any semaphore is created.
    /// </summary>
    private static DistributedSemaphoreRequest[] _MaterializeCanonicalRequests(
        IEnumerable<DistributedSemaphoreRequest> requests
    )
    {
        var canonical = new List<DistributedSemaphoreRequest>();
        var capacities = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            Argument.IsNotNull(request, paramName: nameof(requests));
            Argument.IsNotNullOrWhiteSpace(request.Resource, paramName: nameof(requests));
            Argument.IsGreaterThanOrEqualTo(request.MaxCount, 1, paramName: nameof(requests));

            if (capacities.TryGetValue(request.Resource, out var capacity))
            {
                if (capacity != request.MaxCount)
                {
                    throw new ArgumentException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Resource '{0}' is requested with conflicting maximum holder counts ({1} and {2}). "
                                + "maxCount is a property of the semaphore, not of the acquisition, so one resource cannot "
                                + "have two capacities.",
                            request.Resource,
                            capacity,
                            request.MaxCount
                        ),
                        nameof(requests)
                    );
                }

                continue;
            }

            capacities.Add(request.Resource, request.MaxCount);
            canonical.Add(request);
        }

        Argument.IsNotEmpty(canonical, paramName: nameof(requests));

        return [.. canonical.OrderBy(static request => request.Resource, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Builds the composite's diagnostic identity — the plain ordinal resource join, because capacity is not part of
    /// identity. This name exists in no backend; never pass it to a by-resource provider API.
    /// </summary>
    private static string _GetCompositeResource(IReadOnlyList<DistributedSemaphoreRequest> canonicalRequests)
    {
        return string.Join("+", canonicalRequests.Select(static request => request.Resource));
    }
}
