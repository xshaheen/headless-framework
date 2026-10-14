// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Builds the native-SQL acquire predicate the PostgreSQL and SQL Server claims apply to a row they name directly,
/// by id and token or by id and execution time. A matching token alone is not enough: an EF write can stamp the same
/// <c>UpdatedAt</c> as a racing claim within one clock tick, and a caller can present a token a successful claim
/// already refreshed, so the row must also still be acquirable by this owner. Mirrors the generic-EF
/// <c>WhereCanAcquireUsingDatabaseClock</c>; the two must stay in lockstep.
/// </summary>
internal static class AcquirePredicateSql
{
    /// <summary>
    /// Returns a parenthesized clause that holds when the row is Idle or Queued and is unleased, leased by
    /// <c>@owner</c>, or leased past the statement clock with a <c>Retry</c> node-death policy. The command must bind
    /// <c>@idle</c>, <c>@queued</c>, <c>@owner</c>, and <c>@retry</c>; the dialect replaces
    /// <see cref="SqlDialectTokens.Now"/> with the statement's database clock.
    /// </summary>
    public static string Build(string status, string ownerId, string lockedUntil, string onNodeDeath)
    {
        return $"""
            ({status} = @idle OR {status} = @queued)
              AND ({ownerId} = @owner
                   OR {lockedUntil} IS NULL
                   OR ({lockedUntil} <= {SqlDialectTokens.Now}
                       AND {onNodeDeath} = @retry))
            """;
    }
}
