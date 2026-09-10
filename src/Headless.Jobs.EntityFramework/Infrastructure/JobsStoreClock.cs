// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Reads the store's <b>current statement</b> clock on a supplied context's own connection and transaction.
/// </summary>
/// <remarks>
/// Everywhere else in this store the database clock reaches the caller as an EF-translated <c>DateTime.UtcNow</c> in a
/// projection, which is correct because those reads run in autocommit. That does not hold for a write that joins a
/// caller's already-open transaction: PostgreSQL translates <c>DateTime.UtcNow</c> to <c>now()</c>, which is frozen at
/// TRANSACTION START, so an ambient transaction that opened minutes earlier would hand back an instant from before the
/// row existed. <c>clock_timestamp()</c> and <c>SYSUTCDATETIME()</c> are the per-statement counterparts and are
/// unaffected by the transaction's age.
/// <para>
/// Detection is by EF provider name rather than by which Headless backend package is installed, because the generic EF
/// store (no backend package, CAS claiming) still runs against these same two databases and needs the same anchor.
/// </para>
/// </remarks>
internal static class JobsStoreClock
{
    private const string _NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string _SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    // EF materializes a scalar SqlQueryRaw result from a column literally named "Value".
    private const string _NpgsqlStatementClockSql = """SELECT clock_timestamp() AT TIME ZONE 'UTC' AS "Value" """;
    private const string _SqlServerStatementClockSql = "SELECT SYSUTCDATETIME() AS [Value]";

    /// <summary>
    /// Reads the store's instant as of THIS statement, on <paramref name="dbContext"/>'s connection and inside
    /// whatever transaction it is enlisted in.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The backend has no known statement-clock function. There is no portable substitute: inside a foreign
    /// transaction every EF-translated clock is a transaction-start clock on at least one supported database, and
    /// seeding a schedule position from a transaction-start clock manufactures a false backlog for the definition's
    /// missed-run policy to resolve. Failing here is deliberate — the caller can still insert positioned rows through
    /// the unseeded overload.
    /// </exception>
    public static async Task<DateTime> GetStatementUtcNowAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var providerName = dbContext.Database.ProviderName;

        var sql = providerName switch
        {
            _NpgsqlProvider => _NpgsqlStatementClockSql,
            _SqlServerProvider => _SqlServerStatementClockSql,
            _ => throw new NotSupportedException(
                $"Seeding a cron definition's schedule position needs the store's current-statement clock, and EF "
                    + $"provider '{providerName}' has none registered. Supported backends: "
                    + $"'{_NpgsqlProvider}' (clock_timestamp()) and '{_SqlServerProvider}' (SYSUTCDATETIME())."
            ),
        };

        var storeUtcNow = await dbContext
            .Database.SqlQueryRaw<DateTime>(sql)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        // Both expressions are UTC by construction; the providers surface them as Kind=Unspecified.
        return DateTime.SpecifyKind(storeUtcNow, DateTimeKind.Utc);
    }
}
