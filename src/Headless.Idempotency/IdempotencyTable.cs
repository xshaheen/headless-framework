// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Fencing;
using Headless.Sql;

namespace Headless.Idempotency;

/// <summary>
/// The record table, sequence, and column names the relational store's statements and each provider's schema
/// contribution use, named by the dialect: snake_case on PostgreSQL, PascalCase on SQL Server. A record's lease lives
/// in the five fenced-row lease columns (<see cref="FencedRowTable{TKey}" />); a completed record is one whose lease
/// is settled, so the record has no status column of its own.
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

        LeaseColumns = new FencedRowColumns
        {
            Generation = dialect.Name("LeaseGeneration"),
            State = dialect.Name("LeaseState"),
            ExpiresAt = dialect.Name("LeaseExpiresAt"),
            Holder = dialect.Name("LeaseHolder"),
            TakeoverCount = dialect.Name("LeaseTakeoverCount"),
        };

        TenantId = column("TenantId");
        Key = column("IdempotencyKey");
        FingerprintAlgorithm = column("FingerprintAlgorithm");
        Fingerprint = column("Fingerprint");
        LeaseGeneration = dialect.Quote(LeaseColumns.Generation);
        LeaseState = dialect.Quote(LeaseColumns.State);
        LeaseExpiresAt = dialect.Quote(LeaseColumns.ExpiresAt);
        LeaseHolder = dialect.Quote(LeaseColumns.Holder);
        LeaseTakeoverCount = dialect.Quote(LeaseColumns.TakeoverCount);
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
        LeaseKey = FencedRowKey<IdempotencyRecordKey>.Composite(
            [
                new(
                    dialect.Name("TenantId"),
                    SqlColumnType.KeyText(IdempotencyFieldLimits.TenantIdMaxLength),
                    static key => key.TenantId
                ),
                new(
                    dialect.Name("IdempotencyKey"),
                    SqlColumnType.KeyText(IdempotencyFieldLimits.KeyMaxLength),
                    static key => key.Key
                ),
            ],
            static (reader, ordinal) => new(reader.GetString(ordinal), reader.GetString(ordinal + 1))
        );
    }

    public string Schema { get; }

    public string TableName { get; }

    public string SequenceName { get; }

    public string Table { get; }

    public string Sequence { get; }

    public string TenantId { get; }

    public string Key { get; }

    public string FingerprintAlgorithm { get; }

    public string Fingerprint { get; }

    public string LeaseGeneration { get; }

    public string LeaseState { get; }

    public string LeaseExpiresAt { get; }

    public string LeaseHolder { get; }

    public string LeaseTakeoverCount { get; }

    /// <summary>The lease columns' unquoted names, as <see cref="FencedRowTable{TKey}" /> takes them.</summary>
    public FencedRowColumns LeaseColumns { get; }

    /// <summary>How a record key maps to the key columns, as <see cref="FencedRowTable{TKey}" /> takes it.</summary>
    public FencedRowKey<IdempotencyRecordKey> LeaseKey { get; }

    public string Result { get; }

    public string ResultContract { get; }

    public string RetentionUntil { get; }

    public string RecoveryPoint { get; }

    public string RecoveryState { get; }

    public string RecoveryContract { get; }

    public IReadOnlyList<SqlKeyColumn> KeyColumns { get; }

    public IReadOnlyList<string> KeyColumnNames { get; }

    /// <summary>
    /// Describes the record table to <see cref="FencedRows" />, drawing generations from the store-wide sequence:
    /// a purged key comes back, and a token from before the purge must never match the new row's generations.
    /// </summary>
    public FencedRowTable<IdempotencyRecordKey> CreateLeaseTable(ISqlDialect dialect, int commandTimeoutSeconds) =>
        FencedRowTable<IdempotencyRecordKey>.Create(
            dialect,
            Schema,
            TableName,
            LeaseKey,
            LeaseColumns,
            SequenceName,
            commandTimeoutSeconds: commandTimeoutSeconds
        );

    /// <summary>A lease state as a SQL literal; stored as <c>smallint</c> with the values of <see cref="RowLeaseState" />.</summary>
    public static string StateLiteral(RowLeaseState state) => ((short)state).ToString(CultureInfo.InvariantCulture);
}
