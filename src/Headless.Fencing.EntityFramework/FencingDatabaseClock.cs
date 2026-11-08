// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.Sql;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Headless.Fencing;

/// <summary>The database-clock predicate that <c>UseFencingDatabaseClock</c> maps for the lease candidate queries.</summary>
internal static class FencingDatabaseClock
{
    internal static readonly MethodInfo IsBeforeExpiryMethod = typeof(FencingDatabaseClock).GetMethod(
        nameof(IsBeforeExpiry),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly,
        [typeof(DateTimeOffset?)]
    )!;

    /// <summary>
    /// <see langword="true" /> when the database clock is before <paramref name="expiresAt" />. Translated to
    /// <c>expires_at &gt; clock</c>; a predicate, not a clock value, because EF Core's SQLite provider cannot translate a
    /// <see cref="DateTimeOffset" /> comparison, while the dialect's text instants compare correctly as text.
    /// </summary>
    internal static bool IsBeforeExpiry(DateTimeOffset? expiresAt)
    {
        _ = expiresAt;

        throw new InvalidOperationException(
            "The fencing database clock runs only inside an Entity Framework Core query. Call UseFencingDatabaseClock on the model builder."
        );
    }

    /// <summary>The SQL expression of the database clock in <paramref name="dialect" />.</summary>
    internal static string Clock(ISqlDialect dialect)
    {
        return dialect.DisplayName switch
        {
            "PostgreSQL" => "clock_timestamp()",
            "SQL Server" => "TODATETIMEOFFSET(SYSUTCDATETIME(), 0)",
            // The SQLite dialect stores an instant as text with six fractional digits and an offset; this clock has the
            // same shape, so the text comparison orders instants correctly.
            "SQLite" => "(strftime('%Y-%m-%d %H:%M:%f', 'now') || '000+00:00')",
            _ => throw new NotSupportedException(
                $"The fencing database clock supports the PostgreSQL, SQL Server, and SQLite dialects, not '{dialect.DisplayName}'."
            ),
        };
    }

    internal static SqlExpression Translate(SqlExpression expiresAt, string clock)
    {
        return new SqlBinaryExpression(
            ExpressionType.GreaterThan,
            expiresAt,
            new SqlFragmentExpression(clock),
            typeof(bool),
            typeMapping: null
        );
    }
}
