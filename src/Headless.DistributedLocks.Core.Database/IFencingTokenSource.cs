// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Optional seam that stamps an exclusive (mutex) lock acquisition with a monotonic fencing token. A custom
/// provider implements this over a durable, strictly-increasing sequence (for example a database sequence)
/// so downstream resources can reject writes carrying a stale token. Registering a source is opt-in; when
/// absent, mutex handles carry no fencing token.
/// </summary>
/// <remarks>
/// Tokens are issued only for exclusive locks — shared (reader) acquisitions never request one. The
/// provider calls <see cref="NextAsync"/> after storage acquisition succeeds but before the handle is
/// returned to the caller; if it throws, the provider releases the just-acquired lock and propagates.
/// </remarks>
[PublicAPI]
public interface IFencingTokenSource
{
    /// <summary>
    /// Returns the next strictly-increasing fencing token for <paramref name="resource"/>, or
    /// <see langword="null"/> if this source does not issue a token for it (the handle is then unfenced).
    /// Implementations must guarantee monotonicity across acquisitions and processes for a given resource.
    /// </summary>
    ValueTask<long?> NextAsync(string resource, CancellationToken cancellationToken = default);
}
