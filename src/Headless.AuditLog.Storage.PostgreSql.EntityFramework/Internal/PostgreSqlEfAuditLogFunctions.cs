// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog.PostgreSql.EntityFramework;

/// <summary>Calls the database functions the migration helpers installed beside the audit log table.</summary>
internal static class PostgreSqlEfAuditLogFunctions
{
    /// <summary>
    /// Calls one of the audit log's database functions, named from the table the model maps
    /// <see cref="AuditLogEntry"/> to, and returns its integer result.
    /// </summary>
    public static async Task<int> CallAsync(
        DbContext context,
        Func<string, string> functionName,
        object[] arguments,
        CancellationToken cancellationToken
    )
    {
        var entity =
            context.Model.FindEntityType(typeof(AuditLogEntry))
            ?? throw new InvalidOperationException(
                $"The DbContext `{context.GetType().FullName}` does not map `{nameof(AuditLogEntry)}`. Call `modelBuilder.ConfigureHeadlessAuditLog(this)` in `OnModelCreating`."
            );
        var schema = entity.GetSchema() is { Length: > 0 } configured ? configured : "public";
        var function = PostgreSqlAuditLogSql.Qualify(schema, functionName(entity.GetTableName()!));
        var placeholders = string.Join(
            ", ",
            arguments.Select(static (_, index) => string.Create(CultureInfo.InvariantCulture, $"{{{index}}}"))
        );

#pragma warning disable EF1002 // False positive: the text holds the model's own quoted identifiers and parameter placeholders; every value is a parameter.
        var results = await context
            .Database.SqlQueryRaw<int>($"""SELECT {function}({placeholders}) AS "Value";""", arguments)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002

        return results[0];
    }
}
