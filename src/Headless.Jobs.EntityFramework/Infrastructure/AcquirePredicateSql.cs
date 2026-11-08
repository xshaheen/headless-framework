// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Fencing;
using Headless.Sql;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Builds the native-SQL claim predicates the PostgreSQL and SQL Server claims apply to the rows they select. They
/// mirror the generic-EF <c>WhereCanAcquireUsingDatabaseClock</c> and <c>WhereCanFallbackClaimUsingDatabaseClock</c>;
/// the two must stay in lockstep. Every clause reads the lease over unqualified columns and
/// <see cref="SqlDialectTokens.Now"/>, which the dialect replaces with the statement's database clock.
/// </summary>
internal static class AcquirePredicateSql
{
    private static readonly string _ActiveLiteral = ((short)RowLeaseState.Active).ToString(
        CultureInfo.InvariantCulture
    );

    /// <summary>
    /// Returns a parenthesized clause that holds when the row is Idle or Queued and either has no live lease or is
    /// held by <c>@owner</c>, whatever its node-death policy: such a row never started, and the claim's new
    /// generation fences off the earlier attempt. The command must bind <c>@idle</c>, <c>@queued</c>, and
    /// <c>@owner</c>.
    /// </summary>
    public static string Build(string status, JobsLeaseColumns lease)
    {
        return $"(({status} = @idle OR {status} = @queued) AND ({NotLive(lease)} OR {_HeldByOwner(lease)}))";
    }

    /// <summary>
    /// Returns a parenthesized clause for the fallback sweep: Idle, or Queued with no live lease. An Idle row with a
    /// live lease is taken only when <paramref name="reclaimOwn"/> allows and <c>@owner</c> holds it, as the
    /// generic grant's fence decides. The command must bind <c>@idle</c> and <c>@queued</c>, and <c>@owner</c> when
    /// <paramref name="reclaimOwn"/> is set.
    /// </summary>
    public static string Fallback(string status, JobsLeaseColumns lease, bool reclaimOwn)
    {
        var fence = reclaimOwn ? $"({NotLive(lease)} OR {_HeldByOwner(lease)})" : NotLive(lease);

        return $"(({status} = @idle OR ({status} = @queued AND {NotLive(lease)})) AND {fence})";
    }

    /// <summary>A clause that holds when no attempt holds the row live by the statement clock.</summary>
    public static string NotLive(JobsLeaseColumns lease)
    {
        return $"NOT ({lease.State} = {_ActiveLiteral} AND {lease.ExpiresAt} > {SqlDialectTokens.Now})";
    }

    private static string _HeldByOwner(JobsLeaseColumns lease)
    {
        return $"({lease.State} = {_ActiveLiteral} AND {lease.Holder} = @owner)";
    }
}
