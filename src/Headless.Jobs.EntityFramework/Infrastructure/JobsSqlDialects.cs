// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Sql.Sqlite;
using Headless.Sql.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// The SQL dialect a Jobs context's lease transitions run in, resolved from its EF Core provider.
/// </summary>
/// <remarks>
/// The lease of a job row is a fenced row: claims, renewals, and final writes run as <c>FencedRows</c> transitions,
/// which render SQL for one of the three dialects below and judge expiry by that database's clock. EF Core alone
/// cannot express those transitions, so a Jobs context on any other provider fails when its model is built, before
/// the host serves a request. This is the only place that decides which providers Jobs supports.
/// </remarks>
internal static class JobsSqlDialects
{
    private const string _NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string _SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string _SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>The dialect of <paramref name="context"/>'s database.</summary>
    /// <exception cref="NotSupportedException">The context's provider is not PostgreSQL, SQL Server, or SQLite.</exception>
    internal static ISqlDialect Resolve(DbContext context) => Resolve(context.Database.ProviderName);

    /// <summary>The dialect of the EF Core provider named <paramref name="providerName"/>.</summary>
    /// <exception cref="NotSupportedException">The provider is not PostgreSQL, SQL Server, or SQLite.</exception>
    internal static ISqlDialect Resolve(string? providerName) =>
        providerName switch
        {
            _NpgsqlProvider => PostgreSqlDialect.Instance,
            _SqlServerProvider => SqlServerDialect.Instance,
            _SqliteProvider => SqliteDialect.Instance,
            _ => throw new NotSupportedException(
                $"Headless Jobs on Entity Framework Core supports PostgreSQL ('{_NpgsqlProvider}'), SQL Server "
                    + $"('{_SqlServerProvider}'), and SQLite ('{_SqliteProvider}'), but the Jobs context uses the "
                    + $"'{providerName ?? "unknown"}' provider. Job leases are fenced rows whose claim and settlement "
                    + "SQL exists only for those three databases."
            ),
        };

    /// <summary>Whether <paramref name="dialect"/> is the SQLite dialect.</summary>
    internal static bool IsSqlite(ISqlDialect dialect) => ReferenceEquals(dialect, SqliteDialect.Instance);
}
