// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The complete set of provider state <see cref="CompositeAcquireCoordinator"/> needs, lifted off the concrete
/// provider interface so one coordinator serves the mutex, reader-writer, and semaphore primitives.
/// </summary>
/// <remarks>
/// These values plus a child-acquire delegate are the entire seam. Renewal and release are deliberately absent:
/// the coordinator drives both through <see cref="IDistributedLease"/> members on the children it already holds, so
/// it never needs a by-resource provider call.
/// </remarks>
/// <param name="TimeProvider">Clock for the whole-set deadline, elapsed-time budgeting, and the renewal cadence.</param>
/// <param name="Logger">
/// Passed to <see cref="CompositeDistributedLease"/>, whose disposal must swallow-and-log rather than throw. Note that
/// this is the <em>only</em> path that swallows: rollback after a failed acquisition surfaces cleanup errors instead.
/// </param>
/// <param name="DefaultAcquireTimeout">Applied when the caller does not specify an acquire timeout.</param>
/// <param name="DefaultTimeUntilExpires">Applied when the caller does not specify a lease TTL.</param>
/// <param name="ClampsInfiniteTimeUntilExpires">
/// Whether the backend silently clamps <see cref="Timeout.InfiniteTimeSpan"/> to
/// <paramref name="DefaultTimeUntilExpires"/> for at least one child in this set, so the coordinator must schedule
/// formation renewal on the TTL the backend actually applied rather than on the one the caller asked for.
/// </param>
internal readonly record struct CompositeAcquireEnvironment(
    TimeProvider TimeProvider,
    ILogger Logger,
    TimeSpan DefaultAcquireTimeout,
    TimeSpan DefaultTimeUntilExpires,
    bool ClampsInfiniteTimeUntilExpires = false
)
{
    /// <summary>Snapshots the values off any lock provider, whatever primitive it serves.</summary>
    /// <param name="provider">The provider whose clock, logger, and defaults back this acquisition.</param>
    /// <param name="clampsInfiniteTimeUntilExpires">
    /// See <see cref="ClampsInfiniteTimeUntilExpires"/>. Only the reader-writer composite passes
    /// <see langword="true"/>, and only for a set that holds at least one read entry.
    /// </param>
    internal static CompositeAcquireEnvironment From(
        IDistributedLockEnvironment provider,
        bool clampsInfiniteTimeUntilExpires = false
    )
    {
        return new CompositeAcquireEnvironment(
            provider.TimeProvider,
            provider.Logger,
            provider.DefaultAcquireTimeout,
            provider.DefaultTimeUntilExpires,
            clampsInfiniteTimeUntilExpires
        );
    }

    /// <summary>
    /// The TTL the backend will really apply to this set's children, which is not always the one the caller asked
    /// for. An infinite TTL is honoured by a mutex, a semaphore, and a reader-writer <em>write</em> lock, but a
    /// reader-writer <em>read</em> lock cannot carry one — its hash entry must have a finite expiry or a crashed
    /// reader strands the resource forever — so the provider clamps it to its default. Renewal cadence has to be
    /// derived from this value, not from the option: a set the coordinator believes is non-expiring is a set it never
    /// renews, and a formation long enough to outlive the clamped TTL would then return a lease whose read children
    /// have already expired.
    /// </summary>
    internal TimeSpan GetEffectiveTimeUntilExpires(DistributedLockAcquireOptions options)
    {
        var timeUntilExpires = options.TimeUntilExpires ?? DefaultTimeUntilExpires;

        return ClampsInfiniteTimeUntilExpires && timeUntilExpires == Timeout.InfiniteTimeSpan
            ? DefaultTimeUntilExpires
            : timeUntilExpires;
    }
}

/// <summary>The outcome of a composite acquisition.</summary>
/// <param name="Lease">
/// The formed lease, or <see langword="null"/> when the set could not be formed before the acquire budget elapsed.
/// A canonical set of exactly one item yields the provider's own child lease, not a <see cref="CompositeDistributedLease"/>.
/// </param>
/// <param name="Resource">
/// The composite's diagnostic identity, or the bare resource name on the single-item passthrough path. Never a
/// backend key — see <see cref="IDistributedLease.Resource"/>.
/// </param>
/// <param name="TryOnce">
/// Whether the caller requested a non-blocking single attempt (<see cref="TimeSpan.Zero"/> acquire timeout). Selects
/// the contention-specific timeout exception on the throwing entry points.
/// </param>
internal readonly record struct CompositeAcquireResult(IDistributedLease? Lease, string Resource, bool TryOnce)
{
    /// <summary>
    /// Unwraps the result for the throwing <c>AcquireAllAsync</c> entry points, which differ from their <c>Try</c>
    /// siblings only in turning an unformed set into a timeout exception. A non-blocking single attempt reports
    /// contention rather than an elapsed wait, because no time ever passed.
    /// </summary>
    internal IDistributedLease LeaseOrThrow()
    {
        return Lease
            ?? throw (
                TryOnce
                    ? LockAcquisitionTimeoutException.ForTryOnceContention(Resource)
                    : new LockAcquisitionTimeoutException(Resource)
            );
    }
}
