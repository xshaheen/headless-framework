// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Sql;

namespace Headless.Idempotency;

/// <summary>
/// The record table, sequence, and column names the relational store's statements and each provider's schema
/// contribution use, named by the dialect: snake_case on PostgreSQL, PascalCase on SQL Server.
/// </summary>
internal sealed class IdempotencyTable
{
    public IdempotencyTable(ISqlDialect dialect, string schema)
    {
        Schema = schema;
        TableName = dialect.Name("IdempotencyRecords");
        SequenceName = dialect.Name("IdempotencyRecordGenerations");
        Table = dialect.Qualify(schema, TableName);
        Sequence = dialect.Qualify(schema, SequenceName);

        string column(string pascal) => dialect.Quote(dialect.Name(pascal));

        TenantId = column("TenantId");
        Key = column("IdempotencyKey");
        Status = column("Status");
        FingerprintAlgorithm = column("FingerprintAlgorithm");
        Fingerprint = column("Fingerprint");
        Generation = column("Generation");
        LeaseExpiresAt = column("LeaseExpiresAt");
        Result = column("Result");
        ResultContract = column("ResultContract");
        RetentionUntil = column("RetentionUntil");
        RecoveryPoint = column("RecoveryPoint");
        RecoveryState = column("RecoveryState");
        RecoveryContract = column("RecoveryContract");

        // The key parameter is not named "Key": T-SQL rejects a batch variable that shares a parameter's name, compared
        // case-insensitively, and @key is too likely a name for one.
        KeyColumns = [new(TenantId, "TenantId"), new(Key, "IdempotencyKey")];
        KeyColumnNames = [TenantId, Key];
    }

    public string Schema { get; }

    public string TableName { get; }

    public string SequenceName { get; }

    public string Table { get; }

    public string Sequence { get; }

    public string TenantId { get; }

    public string Key { get; }

    public string Status { get; }

    public string FingerprintAlgorithm { get; }

    public string Fingerprint { get; }

    public string Generation { get; }

    public string LeaseExpiresAt { get; }

    public string Result { get; }

    public string ResultContract { get; }

    public string RetentionUntil { get; }

    public string RecoveryPoint { get; }

    public string RecoveryState { get; }

    public string RecoveryContract { get; }

    public IReadOnlyList<SqlKeyColumn> KeyColumns { get; }

    public IReadOnlyList<string> KeyColumnNames { get; }

    /// <summary>A stored status as a SQL literal; stored as <c>smallint</c> with the values of <see cref="IdempotencyRecordStatus" />.</summary>
    public static string StatusLiteral(IdempotencyRecordStatus status) =>
        ((short)status).ToString(CultureInfo.InvariantCulture);
}
