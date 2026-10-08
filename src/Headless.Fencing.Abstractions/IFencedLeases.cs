// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// Autonomous fenced leases: each call runs in its own transaction on the provider's connection and commits before it
/// returns, so its effect is visible to every other process at once.
/// </summary>
/// <remarks>
/// <para>
/// A lease is identified by the current tenant, a kind, and a resource. Each grant issues a generation strictly
/// greater than any earlier one for that identity, and every later call names the generation it holds, so an attempt
/// that lost its lease (it paused past its expiry, and a takeover or sweep replaced it) is refused rather than
/// trusted. Expiry is decided by the database clock, never by the application's.
/// </para>
/// <para>
/// To make an attempt's own business writes conditional on still holding the lease, fence them inside the unit of
/// work that writes them through <c>unit.Leases.FenceAsync</c>, and settle through <c>unit.Leases.SettleAsync</c>
/// in the same unit. This interface never joins a caller's transaction.
/// </para>
/// </remarks>
[PublicAPI]
public interface IFencedLeases
{
    /// <summary>Grants the lease on <paramref name="resource" /> for <paramref name="duration" />.</summary>
    /// <param name="kind">The lease kind.</param>
    /// <param name="resource">The leased resource within the kind.</param>
    /// <param name="duration">How long the lease lives unless renewed; bounded by the configured limits.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="LeaseGrantStatus.Granted" /> or <see cref="LeaseGrantStatus.Takeover" /> with the new lease, or
    /// <see cref="LeaseGrantStatus.Held" /> with the live holder's generation and expiry. Every result carries the
    /// lease's takeover count, and a grant that resumes an expired attempt carries that attempt's last progress.
    /// </returns>
    /// <exception cref="ArgumentException">The kind, resource, or current tenant id is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    ValueTask<LeaseGrantResult> GrantAsync(
        string kind,
        string resource,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants the lease on <paramref name="resource" /> for <paramref name="duration" />, choosing what happens when
    /// the last attempt expired without ending.
    /// </summary>
    /// <param name="kind">The lease kind.</param>
    /// <param name="resource">The leased resource within the kind.</param>
    /// <param name="duration">How long the lease lives unless renewed; bounded by the configured limits.</param>
    /// <param name="takeover">
    /// Whether an expired attempt is taken over at once, or kept until a sweep abandons it. Pass
    /// <see cref="LeaseTakeover.AfterSweep" /> for work that may already have had an external effect.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// The same results as <see cref="GrantAsync(string, string, TimeSpan, CancellationToken)" />, except that with
    /// <see cref="LeaseTakeover.AfterSweep" /> an expired attempt yields <see cref="LeaseGrantStatus.Expired" /> with
    /// its generation and expiry instead of a takeover.
    /// </returns>
    /// <exception cref="ArgumentException">The kind, resource, or current tenant id is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException">
    /// <paramref name="takeover" /> is not a defined value.
    /// </exception>
    ValueTask<LeaseGrantResult> GrantAsync(
        string kind,
        string resource,
        TimeSpan duration,
        LeaseTakeover takeover,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads whether <paramref name="lease" /> is still the current, live attempt, by the database clock, without
    /// locking or changing anything.
    /// </summary>
    /// <remarks>
    /// The answer can change as soon as it is returned, so use it to drop late work early (a result or event from an
    /// attempt that was already replaced), never to guard a write. A write that must only land while the attempt
    /// still holds the lease runs <c>unit.Leases.FenceAsync</c> in the writing unit of work instead.
    /// </remarks>
    /// <param name="lease">The lease to check.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="LeaseFenceStatus.Current" /> while the attempt holds a live lease, otherwise why it no longer does:
    /// <see cref="LeaseFenceStatus.Expired" />, <see cref="LeaseFenceStatus.Stale" /> (a later generation exists or the
    /// row was purged), <see cref="LeaseFenceStatus.Settled" />, <see cref="LeaseFenceStatus.Released" />, or
    /// <see cref="LeaseFenceStatus.Abandoned" />.
    /// </returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    ValueTask<LeaseFenceStatus> GetStatusAsync(FencedLease lease, CancellationToken cancellationToken = default);

    /// <summary>Moves a live lease's expiry to <paramref name="duration" /> from now, by the database clock.</summary>
    /// <remarks>Any progress recorded by an earlier renewal is kept.</remarks>
    /// <param name="lease">The lease to renew.</param>
    /// <param name="duration">The new time to live; bounded by the configured limits.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="LeaseRenewalStatus.Renewed" /> with the new expiry, or why the lease was not extended.
    /// </returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    ValueTask<LeaseRenewalResult> RenewAsync(
        FencedLease lease,
        TimeSpan duration,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Moves a live lease's expiry to <paramref name="duration" /> from now, by the database clock, and records how
    /// far the attempt got.
    /// </summary>
    /// <remarks>
    /// <paramref name="progress" /> is stored in the same guarded write that extends the lease, so it is recorded
    /// only when the renewal succeeds: a stale, expired, or ended attempt never overwrites it. A later
    /// <see cref="LeaseGrantStatus.Takeover" /> grant returns the last recorded progress so the new attempt can resume.
    /// </remarks>
    /// <param name="lease">The lease to renew.</param>
    /// <param name="duration">The new time to live; bounded by the configured limits.</param>
    /// <param name="progress">How far the attempt got, replacing any progress recorded before.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns>
    /// <see cref="LeaseRenewalStatus.Renewed" /> with the new expiry, or why the lease was not extended and the
    /// progress not recorded.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="progress" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// The lease's identity or generation is invalid, or <paramref name="progress" /> exceeds its size limits.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is outside the configured bounds.</exception>
    ValueTask<LeaseRenewalResult> RenewAsync(
        FencedLease lease,
        TimeSpan duration,
        LeaseProgress progress,
        CancellationToken cancellationToken = default
    );

    /// <summary>Settles the attempt: its final write, after which the lease is no longer held.</summary>
    /// <param name="lease">The lease to settle.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns><see cref="LeaseSettlementStatus.Settled" /> on success, or why the settlement was refused.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    ValueTask<LeaseSettlementStatus> SettleAsync(FencedLease lease, CancellationToken cancellationToken = default);

    /// <summary>Gives the lease up without a result, so the next grant succeeds at once.</summary>
    /// <param name="lease">The lease to release.</param>
    /// <param name="cancellationToken">Token used to cancel the database call.</param>
    /// <returns><see cref="LeaseSettlementStatus.Released" /> on success, or why the release was refused.</returns>
    /// <exception cref="ArgumentException">The lease's identity or generation is invalid.</exception>
    ValueTask<LeaseSettlementStatus> ReleaseAsync(FencedLease lease, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims expired, still-active leases of <paramref name="kind" /> across every tenant, one at a time, marks each
    /// abandoned, and hands it to <paramref name="handler" /> inside the same transaction.
    /// </summary>
    /// <remarks>
    /// Each lease gets its own owned unit of work: the claim, the handler's writes, and the commit are one
    /// transaction, so the handoff is durable only if the handler's writes are. Write the handoff through the unit
    /// (<c>unit.Outbox</c>, <c>unit.Jobs</c>, or raw ADO.NET or Dapper on its connection); an EF Core context cannot
    /// join a unit that owns a raw connection. A handler that throws rolls back only its own lease, which stays
    /// expired and is offered again by a later call, never again by this one. The sweep never re-runs the work
    /// itself, and concurrent sweepers hand each lease to exactly one committed handler. To claim inside a unit of
    /// work you begin, such as one over an EF Core context, call <c>unit.Leases.ClaimExpiredAsync</c> instead.
    /// </remarks>
    /// <param name="kind">The lease kind to sweep.</param>
    /// <param name="handler">Routes one abandoned attempt, writing through the unit it is given.</param>
    /// <param name="limit">The most leases this call claims.</param>
    /// <param name="cancellationToken">Token used to cancel the sweep and each handler.</param>
    /// <returns>The leases handed off, and those whose handler threw.</returns>
    /// <exception cref="ArgumentException"><paramref name="kind" /> is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit" /> is not positive.</exception>
    ValueTask<LeaseSweepResult> SweepExpiredAsync(
        string kind,
        Func<ExpiredLease, IUnitOfWork, CancellationToken, ValueTask> handler,
        int limit,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes the settled, released, and abandoned leases of <paramref name="kind" /> that ended at least
    /// <paramref name="olderThan" /> ago, by the database clock. Live and expired-but-active leases are kept.
    /// </summary>
    /// <remarks>
    /// Purging never lets a generation repeat: a lease granted again after its row was deleted still gets a
    /// generation above every earlier one.
    /// </remarks>
    /// <param name="kind">The lease kind to purge.</param>
    /// <param name="olderThan">How long a lease must have been over before it is deleted; zero or more.</param>
    /// <param name="cancellationToken">Token used to cancel the database calls.</param>
    /// <returns>The number of leases deleted.</returns>
    /// <exception cref="ArgumentException"><paramref name="kind" /> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="olderThan" /> is negative.</exception>
    ValueTask<int> PurgeAsync(string kind, TimeSpan olderThan, CancellationToken cancellationToken = default);
}
