// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

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
    /// <param name="resource">The resource whose exclusive acquisition is being stamped.</param>
    /// <param name="connection">
    /// Optional open connection already owned by the just-acquired lock handle. When supplied and compatible with the
    /// source's backend, the implementation may issue the token on it to avoid opening a second connection; when
    /// <see langword="null"/> (or incompatible), the implementation opens its own connection.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel issuing the token.</param>
    ValueTask<long?> NextAsync(
        string resource,
        DbConnection? connection = null,
        CancellationToken cancellationToken = default
    );
}
