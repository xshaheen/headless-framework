// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences.SqlServer;

/// <summary>Column names of the counter table, shared by the initializer's DDL and the store's statement.</summary>
internal static class SqlServerSequencesSchema
{
    public const string TenantId = "[tenant_id]";
    public const string Name = "[name]";
    public const string Partition = "[partition]";
    public const string Value = "[value]";
    public const string CreatedAt = "[created_at]";
    public const string UpdatedAt = "[updated_at]";

    // Binary code-point order, so counter names, partitions, and tenant ids match case- and accent-sensitively
    // whatever the database's default collation is. It does not stop SQL Server padding trailing spaces before
    // comparing, so 'a' and 'a ' still collide here; key parts are refused when they start or end with
    // whitespace, which is what makes matching ordinal, the same as the PostgreSQL provider.
    public const string KeyCollation = "Latin1_General_100_BIN2";

    public static string Qualified(SqlServerSequencesOptions options)
    {
        return $"[{options.Schema}].[{options.TableName}]";
    }
}
