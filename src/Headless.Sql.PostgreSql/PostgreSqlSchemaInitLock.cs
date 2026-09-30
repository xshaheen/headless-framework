// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql.PostgreSql;

/// <summary>
/// The schema-wide advisory lock every Headless PostgreSQL storage initializer takes before
/// <c>CREATE SCHEMA</c>.
/// </summary>
/// <remarks>
/// Features share one schema but each locks only its own objects. Without a common lock, a foreign feature's
/// concurrent <c>CREATE SCHEMA</c> fails this feature's DDL transaction with <c>42P06</c>/<c>23505</c>, and absorbing
/// that as "already exists" rolls back this feature's tables along with it. Every initializer must build the lock
/// through this one statement: a key that drifted in a single feature would stop that feature serializing against
/// the others. Take it inside the DDL transaction, after the feature's own lock, so the lock order is the same
/// everywhere.
/// </remarks>
[PublicAPI]
public static class PostgreSqlSchemaInitLock
{
    /// <summary>
    /// Returns the statement that takes the transaction-scoped schema-wide lock for <paramref name="schema"/>.
    /// </summary>
    /// <param name="schema">The already-validated schema identifier the initializer is about to create.</param>
    /// <returns>A single <c>SELECT pg_advisory_xact_lock(...)</c> statement terminated by a semicolon.</returns>
    public static string AcquireStatement(string schema)
    {
        return $"SELECT pg_advisory_xact_lock(hashtextextended('headless_schema_init:{schema}', 0));";
    }
}
