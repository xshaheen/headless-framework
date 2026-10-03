// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

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
