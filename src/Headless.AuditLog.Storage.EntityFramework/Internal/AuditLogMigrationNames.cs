// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;

namespace Headless.AuditLog.Internal;

/// <summary>
/// Resolves and checks the names a migration helper splices into DDL, by the same rules the storage options apply.
/// Shared by the database-specific Entity Framework audit packages.
/// </summary>
internal static class AuditLogMigrationNames
{
    /// <summary>Returns the audit log table name for <paramref name="provider"/>, after checking both names.</summary>
    /// <param name="provider">The database the migration targets.</param>
    /// <param name="schema">The table's schema.</param>
    /// <param name="table">The configured table name; <see langword="null"/> for the database's conventional name.</param>
    /// <exception cref="ArgumentException">A name is not a valid identifier for the database.</exception>
    public static string Resolve(StorageProvider provider, string schema, string? table)
    {
        var style =
            provider is StorageProvider.PostgreSql ? StorageNamingStyle.SnakeCase : StorageNamingStyle.PascalCase;
        var tableName = HeadlessStorageNaming.Resolve(table, style, AuditLogStorageNames.DefaultTableName);
        var (pattern, maxLength) = StorageIdentifier.For(provider);

        Argument.IsNotNullOrWhiteSpace(schema);
        Argument.IsTrue(
            schema.Length <= maxLength && pattern.IsMatch(schema),
            $"'{schema}' is not a valid {provider} identifier.",
            nameof(schema)
        );
        Argument.IsTrue(
            tableName.Length <= maxLength && pattern.IsMatch(tableName),
            $"'{tableName}' is not a valid {provider} identifier.",
            nameof(table)
        );

        return tableName;
    }

    /// <summary>Refuses a migration that targets another database than <paramref name="expected"/>.</summary>
    /// <exception cref="NotSupportedException">The migration's database is not <paramref name="expected"/>.</exception>
    public static void EnsureProvider(string? activeProvider, StorageProvider expected, string package)
    {
        if (EfAuditLogDatabase.Provider(activeProvider) != expected)
        {
            throw new NotSupportedException(
                $"The audit log migration helpers of {package} are for {expected}, not for '{activeProvider}'."
            );
        }
    }
}
