// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency.PostgreSql;

/// <summary>Object and column names of the record storage, shared by the initializer's DDL and the store's statements.</summary>
internal static class PostgreSqlIdempotencySchema
{
    public const string TableName = "records";
    public const string SequenceName = "record_generations";

    public const string TenantId = "tenant_id";
    public const string Key = "idempotency_key";
    public const string Status = "status";
    public const string FingerprintAlgorithm = "fingerprint_algorithm";
    public const string Fingerprint = "fingerprint";
    public const string Generation = "generation";
    public const string LeaseExpiresAt = "lease_expires_at";
    public const string Result = "result";
    public const string ResultContract = "result_contract";
    public const string RetentionUntil = "retention_until";

    // Stored as smallint, with the values of IdempotencyRecordStatus.
    public const short Pending = (short)IdempotencyRecordStatus.Pending;
    public const short Completed = (short)IdempotencyRecordStatus.Completed;

    public static string QualifiedTable(string schema)
    {
        return $"\"{schema}\".\"{TableName}\"";
    }

    public static string QualifiedSequence(string schema)
    {
        return $"\"{schema}\".\"{SequenceName}\"";
    }
}
