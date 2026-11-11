// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog.SqlServer;

/// <summary>
/// The SQL Server DDL behind an append-only audit log table. This provider's schema step and the SQL Server Entity
/// Framework migration helper both emit it, so a table created by either carries the same trigger.
/// </summary>
/// <remarks>
/// Every name is spliced into the SQL, so callers pass only identifiers that already passed the storage identifier
/// rules.
/// </remarks>
internal static class SqlServerAuditLogSql
{
    /// <summary>The append-only trigger on the table.</summary>
    public static string AppendOnlyTrigger(string table) => $"TR_{table}_{AuditLogStorageNames.AppendOnlyTrigger}";

    /// <summary>
    /// The append-only guard: one trigger that refuses every <c>UPDATE</c>, and every <c>DELETE</c> run without the
    /// purge's session key.
    /// </summary>
    /// <remarks>
    /// The SQL Server provider records this text as a schema step, and the runner compares its checksum on every start.
    /// Changing one character fails the start of every host that already applied it.
    /// </remarks>
    public static string AppendOnly(string schema, string table)
    {
        var name = AppendOnlyTrigger(table);

        // AFTER UPDATE, DELETE with THROW rolls the statement back. A DELETE (no inserted rows) passes only while the
        // purge has the session key set. TRUNCATE fires no trigger on SQL Server, so it stays the job of permissions.
        // CREATE TRIGGER must start its batch, hence EXEC.
        var body = $"""
            CREATE OR ALTER TRIGGER [{schema}].[{name}] ON [{schema}].[{table}] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF NOT EXISTS (SELECT 1 FROM inserted)
                    AND CONVERT(int, SESSION_CONTEXT(N'{AuditLogStorageNames.PurgeSetting}')) = 1
                    RETURN;
                THROW 50000, N'The audit log table {schema}.{table} is append-only: only the retention purge may delete rows.', 1;
            END
            """;

        return $"EXEC(N'{body.Replace("'", "''", StringComparison.Ordinal)}');";
    }

    /// <summary>Removes the trigger <see cref="AppendOnly"/> created.</summary>
    public static string DropAppendOnly(string schema, string table)
    {
        return $"DROP TRIGGER IF EXISTS [{schema}].[{AppendOnlyTrigger(table)}];";
    }
}
