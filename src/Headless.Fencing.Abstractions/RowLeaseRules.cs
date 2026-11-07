// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.Fencing;

/// <summary>
/// The row lease's decisions as pure functions over a <see cref="RowLeaseSnapshot" /> and a supplied clock: the same
/// rules the relational row operations render as SQL, so an in-memory store that keeps the five lease fields on its
/// own objects answers exactly as a database would.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here mutates or locks. The caller decides under its own lock and stores
/// <see cref="RowLeaseTransition{TStatus}.Next" /> when <see cref="RowLeaseTransition{TStatus}.Wrote" /> is set.
/// </para>
/// <para>
/// A lease is live while it is <see cref="RowLeaseState.Active" /> and its expiry is after <c>now</c>. A generation is
/// judged against the row's own: any other generation is <see cref="LeaseFenceStatus.Stale" />, and the row's
/// generation reports the state its attempt ended in, so a revoked generation answers
/// <see cref="LeaseFenceStatus.Revoked" /> to every verb.
/// </para>
/// </remarks>
[PublicAPI]
public static class RowLeaseRules
{
    /// <summary>Grants the row to a new attempt unless a live attempt holds it.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <param name="now">The clock that judges expiry.</param>
    /// <param name="duration">How long the new attempt holds the row.</param>
    /// <param name="options">The takeover policy and the holder to store.</param>
    /// <param name="nextGeneration">
    /// The generation to issue, drawn from a store-wide sequence, or <see langword="null" /> to issue the row's
    /// generation plus one.
    /// </param>
    /// <returns>
    /// <see cref="LeaseGrantStatus.Granted" /> over an idle row or an ended attempt, and over a live attempt whose
    /// holder equals (ordinally) a non-blank <see cref="LeaseGrantOptions.Holder" />, which is handed back unchanged
    /// with nothing written. <see cref="LeaseGrantStatus.Takeover" /> over an expired active attempt, counting one more
    /// takeover, or <see cref="LeaseGrantStatus.Expired" /> there under <see cref="LeaseTakeover.AfterSweep" />.
    /// <see cref="LeaseGrantStatus.Held" /> over any other live attempt.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="duration" /> is not positive, or <paramref name="nextGeneration" /> is not above the row's
    /// generation.
    /// </exception>
    public static RowLeaseTransition<LeaseGrantStatus> Grant(
        RowLeaseSnapshot lease,
        DateTimeOffset now,
        TimeSpan duration,
        LeaseGrantOptions options,
        long? nextGeneration = null
    )
    {
        Argument.IsNotNull(options);
        Argument.IsPositive(duration);

        if (nextGeneration is { } drawn)
        {
            Argument.IsGreaterThan(drawn, lease.Generation, paramName: nameof(nextGeneration));
        }

        if (_IsLive(lease, now))
        {
            // The caller's own attempt is handed back as it is, so a grant retried after a lost response is idempotent;
            // the expiry is not extended, which is what renew is for.
            var own =
                !string.IsNullOrWhiteSpace(options.Holder)
                && string.Equals(lease.Holder, options.Holder, StringComparison.Ordinal);

            return new(own ? LeaseGrantStatus.Granted : LeaseGrantStatus.Held, lease, Wrote: false);
        }

        var expiredAttempt = lease.State == RowLeaseState.Active;

        if (expiredAttempt && options.Takeover == LeaseTakeover.AfterSweep)
        {
            return new(LeaseGrantStatus.Expired, lease, Wrote: false);
        }

        // Only a takeover of an expired active attempt counts one more: a sweep already counted an abandoned one, and
        // a settlement, release, or revocation reset the count.
        var next = new RowLeaseSnapshot(
            nextGeneration ?? (lease.Generation + 1),
            RowLeaseState.Active,
            now + duration,
            options.Holder,
            expiredAttempt ? lease.TakeoverCount + 1 : lease.TakeoverCount
        );

        return new(expiredAttempt ? LeaseGrantStatus.Takeover : LeaseGrantStatus.Granted, next, Wrote: true);
    }

    /// <summary>Extends a live generation's expiry to <paramref name="now" /> plus <paramref name="duration" />.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <param name="generation">The generation the caller holds.</param>
    /// <param name="now">The clock that judges expiry.</param>
    /// <param name="duration">How long the attempt holds the row from <paramref name="now" />.</param>
    /// <returns><see cref="LeaseRenewalStatus.Renewed" /> for a live generation; otherwise why it was refused.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration" /> is not positive.</exception>
    public static RowLeaseTransition<LeaseRenewalStatus> Renew(
        RowLeaseSnapshot lease,
        long generation,
        DateTimeOffset now,
        TimeSpan duration
    )
    {
        Argument.IsPositive(duration);

        var status = Judge(lease, generation, now) switch
        {
            LeaseFenceStatus.Current => LeaseRenewalStatus.Renewed,
            LeaseFenceStatus.Expired => LeaseRenewalStatus.Expired,
            LeaseFenceStatus.Settled => LeaseRenewalStatus.Settled,
            LeaseFenceStatus.Released => LeaseRenewalStatus.Released,
            LeaseFenceStatus.Abandoned => LeaseRenewalStatus.Abandoned,
            LeaseFenceStatus.Revoked => LeaseRenewalStatus.Revoked,
            _ => LeaseRenewalStatus.Stale,
        };

        return status == LeaseRenewalStatus.Renewed
            ? new(status, lease with { ExpiresAt = now + duration }, Wrote: true)
            : new(status, lease, Wrote: false);
    }

    /// <summary>Ends a live generation's attempt as settled: its work finished.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <param name="generation">The generation the caller holds.</param>
    /// <param name="now">The clock that judges expiry.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Settled" /> for a live generation, and again for a generation that already
    /// settled, so a retried settlement succeeds; otherwise why it was refused.
    /// </returns>
    public static RowLeaseTransition<LeaseSettlementStatus> Settle(
        RowLeaseSnapshot lease,
        long generation,
        DateTimeOffset now
    )
    {
        return _End(lease, generation, now, RowLeaseState.Settled, LeaseSettlementStatus.Settled);
    }

    /// <summary>Ends a live generation's attempt as released, so the row can be granted again at once.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <param name="generation">The generation the caller holds.</param>
    /// <param name="now">The clock that judges expiry.</param>
    /// <returns>
    /// <see cref="LeaseSettlementStatus.Released" /> for a live generation, and again for a generation already
    /// released; <see cref="LeaseSettlementStatus.Settled" /> for one that settled; otherwise why it was refused.
    /// </returns>
    public static RowLeaseTransition<LeaseSettlementStatus> Release(
        RowLeaseSnapshot lease,
        long generation,
        DateTimeOffset now
    )
    {
        return _End(lease, generation, now, RowLeaseState.Released, LeaseSettlementStatus.Released);
    }

    /// <summary>Ends the active attempt, live or expired, without its generation.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <returns>
    /// <see cref="LeaseFenceStatus.Revoked" /> with <see cref="RowLeaseTransition{TStatus}.Wrote" /> set when an
    /// active attempt was ended. With nothing active it writes nothing and reports how the row's last attempt ended
    /// (<see cref="LeaseFenceStatus.Stale" /> for a row never granted), so a second revocation reports
    /// <see cref="LeaseFenceStatus.Revoked" /> without writing.
    /// </returns>
    public static RowLeaseTransition<LeaseFenceStatus> Revoke(RowLeaseSnapshot lease)
    {
        if (lease.State != RowLeaseState.Active)
        {
            return new(_Ended(lease), lease, Wrote: false);
        }

        return new(
            LeaseFenceStatus.Revoked,
            lease with
            {
                State = RowLeaseState.Revoked,
                TakeoverCount = 0,
            },
            Wrote: true
        );
    }

    /// <summary>Judges whether <paramref name="generation" /> may still write, as a fence read would.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <param name="generation">The generation the caller holds.</param>
    /// <param name="now">The clock that judges expiry.</param>
    /// <returns>
    /// <see cref="LeaseFenceStatus.Current" /> for the row's live generation, <see cref="LeaseFenceStatus.Expired" />
    /// for its expired active one, the ended state for its ended one, and <see cref="LeaseFenceStatus.Stale" /> for
    /// any other generation.
    /// </returns>
    public static LeaseFenceStatus Judge(RowLeaseSnapshot lease, long generation, DateTimeOffset now)
    {
        if (lease.Generation != generation)
        {
            return LeaseFenceStatus.Stale;
        }

        if (lease.State == RowLeaseState.Active)
        {
            return _IsLive(lease, now) ? LeaseFenceStatus.Current : LeaseFenceStatus.Expired;
        }

        return _Ended(lease);
    }

    /// <summary>Abandons an active attempt whose expiry has passed, counting one more takeover, as a sweep does.</summary>
    /// <param name="lease">The stored lease.</param>
    /// <param name="now">The clock that judges expiry.</param>
    /// <returns>
    /// <see cref="LeaseFenceStatus.Abandoned" /> with <see cref="RowLeaseTransition{TStatus}.Wrote" /> set for an
    /// expired active attempt; otherwise the row's own generation judged at <paramref name="now" />, with nothing
    /// written.
    /// </returns>
    public static RowLeaseTransition<LeaseFenceStatus> AbandonIfExpired(RowLeaseSnapshot lease, DateTimeOffset now)
    {
        if (lease.State != RowLeaseState.Active || _IsLive(lease, now))
        {
            return new(Judge(lease, lease.Generation, now), lease, Wrote: false);
        }

        return new(
            LeaseFenceStatus.Abandoned,
            lease with
            {
                State = RowLeaseState.Abandoned,
                TakeoverCount = lease.TakeoverCount + 1,
            },
            Wrote: true
        );
    }

    private static RowLeaseTransition<LeaseSettlementStatus> _End(
        RowLeaseSnapshot lease,
        long generation,
        DateTimeOffset now,
        RowLeaseState end,
        LeaseSettlementStatus success
    )
    {
        // A settled or released generation reports its own state whichever verb asked, so a retried settlement
        // succeeds and a release after a settlement reports that the attempt settled. A revoked one is never success.
        var status = Judge(lease, generation, now) switch
        {
            LeaseFenceStatus.Current => success,
            LeaseFenceStatus.Settled => LeaseSettlementStatus.Settled,
            LeaseFenceStatus.Released => LeaseSettlementStatus.Released,
            LeaseFenceStatus.Abandoned => LeaseSettlementStatus.Abandoned,
            LeaseFenceStatus.Revoked => LeaseSettlementStatus.Revoked,
            LeaseFenceStatus.Expired => LeaseSettlementStatus.Expired,
            _ => LeaseSettlementStatus.Stale,
        };

        if (lease.Generation != generation || lease.State != RowLeaseState.Active || status != success)
        {
            return new(status, lease, Wrote: false);
        }

        // An ended attempt's takeovers belong to work that is finished, so the next grant starts the count again.
        return new(status, lease with { State = end, TakeoverCount = 0 }, Wrote: true);
    }

    private static bool _IsLive(RowLeaseSnapshot lease, DateTimeOffset now)
    {
        return lease.State == RowLeaseState.Active && lease.ExpiresAt > now;
    }

    private static LeaseFenceStatus _Ended(RowLeaseSnapshot lease)
    {
        return lease.State switch
        {
            // Generation 0 is never issued, so no caller holds an idle row's generation.
            RowLeaseState.Idle => LeaseFenceStatus.Stale,
            RowLeaseState.Active => LeaseFenceStatus.Current,
            RowLeaseState.Settled => LeaseFenceStatus.Settled,
            RowLeaseState.Released => LeaseFenceStatus.Released,
            RowLeaseState.Abandoned => LeaseFenceStatus.Abandoned,
            RowLeaseState.Revoked => LeaseFenceStatus.Revoked,
            _ => throw new InvalidOperationException(
                $"The row lease carries an unknown state {((short)lease.State).ToString(CultureInfo.InvariantCulture)}."
            ),
        };
    }
}
