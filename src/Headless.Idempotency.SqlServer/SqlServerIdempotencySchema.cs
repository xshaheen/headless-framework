// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency.SqlServer;

/// <summary>Object and column names of the record storage, shared by the initializer's DDL and the store's statements.</summary>
internal static class SqlServerIdempotencySchema
{
    public const string TableName = "records";

    public const string TenantId = "[tenant_id]";
    public const string Key = "[idempotency_key]";
    public const string Status = "[status]";
    public const string FingerprintAlgorithm = "[fingerprint_algorithm]";
    public const string Fingerprint = "[fingerprint]";
    public const string LeaseGeneration = "[lease_generation]";
    public const string Result = "[result]";
    public const string ResultContract = "[result_contract]";
    public const string RetentionUntil = "[retention_until]";

    // Stored as smallint, with the values of IdempotencyRecordStatus.
    public const short Pending = (short)IdempotencyRecordStatus.Pending;
    public const short Completed = (short)IdempotencyRecordStatus.Completed;

    // Binary code-point order, so keys and tenant ids match case- and accent-sensitively whatever the database's
    // default collation is. It does not stop SQL Server padding trailing spaces before comparing, so 'a' and 'a '
    // would still collide; keys and tenant ids are refused when they start or end with whitespace, which is what makes
    // matching ordinal, the same as the PostgreSQL provider.
    public const string KeyCollation = "Latin1_General_100_BIN2";

    public static string QualifiedTable(string schema)
    {
        return $"[{schema}].[{TableName}]";
    }
}
