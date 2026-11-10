// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog.Internal;

/// <summary>What the EF audit log services need to know about the database behind a context.</summary>
internal static class EfAuditLogDatabase
{
    public const string SqlServerEfProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    public static StorageProvider? Provider(DbContext context) => Provider(context.Database.ProviderName);

    public static StorageProvider? Provider(string? providerName)
    {
        return providerName switch
        {
            HeadlessStorageNaming.NpgsqlEfProviderName => StorageProvider.PostgreSql,
            SqlServerEfProviderName => StorageProvider.SqlServer,
            _ => null,
        };
    }

    /// <summary>Refuses a database other than PostgreSQL, naming the <paramref name="feature"/> that needs it.</summary>
    /// <exception cref="NotSupportedException"><paramref name="provider"/> is not PostgreSQL.</exception>
    public static void EnsurePostgreSql(StorageProvider? provider, string feature)
    {
        if (provider is not StorageProvider.PostgreSql)
        {
            throw new NotSupportedException($"{feature} is supported on PostgreSQL only.");
        }
    }

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
