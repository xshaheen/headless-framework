// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency.SqlServer;

/// <summary>Object and column names of the record storage, shared by the initializer's DDL and the store's statements.</summary>
internal static class SqlServerIdempotencySchema
{
    public const string TableName = "IdempotencyRecords";
    public const string SequenceName = "IdempotencyRecordGenerations";

    public const string TenantId = "[TenantId]";
    public const string Key = "[IdempotencyKey]";
    public const string Status = "[Status]";
    public const string FingerprintAlgorithm = "[FingerprintAlgorithm]";
    public const string Fingerprint = "[Fingerprint]";
    public const string Generation = "[Generation]";
    public const string LeaseExpiresAt = "[LeaseExpiresAt]";
    public const string Result = "[Result]";
    public const string ResultContract = "[ResultContract]";
    public const string RetentionUntil = "[RetentionUntil]";
    public const string RecoveryPoint = "[RecoveryPoint]";
    public const string RecoveryState = "[RecoveryState]";
    public const string RecoveryContract = "[RecoveryContract]";

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

    public static string QualifiedSequence(string schema)
    {
        return $"[{schema}].[{SequenceName}]";
    }
}
