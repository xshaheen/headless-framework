// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.PostgreSql;
using Headless.Checks;
using Headless.Hosting;

namespace Headless.AuditLog;

/// <summary>
/// The database functions that keep the monthly partitions of a partitioned audit log table on PostgreSQL:
/// <c>{table}_ensure_partitions</c> and <c>{table}_drop_partitions</c>, which
/// <see cref="IAuditLogPartitions{TContext}"/> calls.
/// </summary>
/// <remarks>
/// Both functions run with their owner's rights and are closed to <c>PUBLIC</c>, and the framework grants them to
/// nobody: which login runs the application is the operator's decision.
/// </remarks>
[PublicAPI]
public static class PostgreSqlAuditLogPartitionFunctions
{
    /// <summary>
    /// Returns the statement that lets <paramref name="role"/> call both partition functions. Run it as their owner,
    /// in a migration or a deploy step, once the functions and the role exist.
    /// </summary>
    /// <param name="role">The login the application connects as. It is quoted, so any role name is accepted.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for <c>audit_log_entries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <returns>A single <c>GRANT EXECUTE ON FUNCTION ... TO ...</c> statement terminated by a semicolon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="role"/> or <paramref name="schema"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="role"/> is empty or longer than a PostgreSQL identifier, or <paramref name="schema"/> or
    /// <paramref name="table"/> is not a valid PostgreSQL identifier.
    /// </exception>
    public static string GrantExecuteStatement(
        string role,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNullOrWhiteSpace(role);
        Argument.IsNotNullOrWhiteSpace(schema);

        var tableName = HeadlessStorageNaming.Resolve(
            table,
            StorageNamingStyle.SnakeCase,
            AuditLogStorageNames.DefaultTableName
        );
        var pattern = StorageIdentifier.PostgreSql.IdentifierPattern;
        const int maxLength = StorageIdentifier.PostgreSql.IdentifierMaxLength;

        Argument.IsTrue(
            role.Length <= maxLength,
            $"'{role}' is longer than a PostgreSQL identifier ({maxLength} characters).",
            nameof(role)
        );
        Argument.IsTrue(
            schema.Length <= maxLength && pattern.IsMatch(schema),
            $"'{schema}' is not a valid PostgreSQL identifier.",
            nameof(schema)
        );
        Argument.IsTrue(
            tableName.Length <= maxLength && pattern.IsMatch(tableName),
            $"'{tableName}' is not a valid PostgreSQL identifier.",
            nameof(table)
        );

        return PostgreSqlAuditLogSql.GrantPartitionFunctions(schema, tableName, role);
    }
}
