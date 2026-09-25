// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences.PostgreSql;

/// <summary>Column names of the counter table, shared by the initializer's DDL and the store's statement.</summary>
internal static class PostgreSqlSequencesSchema
{
    public const string TenantId = "tenant_id";
    public const string Name = "name";

    // Quoted wherever it appears: PARTITION is a keyword in PostgreSQL's grammar.
    public const string Partition = "\"partition\"";
    public const string Value = "value";
    public const string CreatedAt = "created_at";
    public const string UpdatedAt = "updated_at";

    public static string Qualified(PostgreSqlSequencesOptions options)
    {
        return $"\"{options.Schema}\".\"{options.TableName}\"";
    }
}
