// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing;
using Headless.Sql;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Persistence;

/// <summary>
/// The messaging tables and columns the relational storage's statements name, in the dialect's naming convention
/// (snake_case on PostgreSQL, PascalCase on SQL Server) and quoted. They are the names each provider's schema
/// contribution creates.
/// </summary>
internal sealed class MessagingTables
{
    public MessagingTables(ISqlDialect dialect, string schema, int leaseHolderMaxLength)
    {
        Argument.IsNotNull(dialect);
        Argument.IsNotNullOrWhiteSpace(schema);

        Published = dialect.Qualify(schema, dialect.Name("MessagingPublished"));
        Received = dialect.Qualify(schema, dialect.Name("MessagingReceived"));

        // The lease columns follow the dialect's naming, so PostgreSQL gets fencing's own lease_* names and SQL Server
        // the PascalCase it uses for every other messaging column.
        var leaseColumns = new FencedRowColumns
        {
            Generation = dialect.Name("LeaseGeneration"),
            State = dialect.Name("LeaseState"),
            ExpiresAt = dialect.Name("LeaseExpiresAt"),
            Holder = dialect.Name("LeaseHolder"),
            TakeoverCount = dialect.Name("LeaseTakeoverCount"),
        };
        var leaseKey = FencedRowKey<Guid>.Scalar(dialect.Name("Id"), SqlColumnType.Guid);
        PublishedLease = FencedRowTable<Guid>.Create(
            dialect,
            schema,
            dialect.Name("MessagingPublished"),
            leaseKey,
            leaseColumns,
            holderMaxLength: leaseHolderMaxLength
        );
        ReceivedLease = FencedRowTable<Guid>.Create(
            dialect,
            schema,
            dialect.Name("MessagingReceived"),
            leaseKey,
            leaseColumns,
            holderMaxLength: leaseHolderMaxLength
        );
        Receipts = dialect.Qualify(schema, dialect.Name("MessagingInboxOperationReceipts"));
        Audit = dialect.Qualify(schema, dialect.Name("MessagingInboxAudit"));
        True = dialect.BooleanLiteral(value: true);
        False = dialect.BooleanLiteral(value: false);

        string column(string pascal) => dialect.Quote(dialect.Name(pascal));

        Id = column("Id");
        Version = column("Version");
        Name = column("Name");
        Content = column("Content");
        IntentType = column("IntentType");
        Retries = column("Retries");
        InlineAttempts = column("InlineAttempts");
        Added = column("Added");
        ExpiresAt = column("ExpiresAt");
        NextRetryAt = column("NextRetryAt");
        LeaseGeneration = column("LeaseGeneration");
        LeaseState = column("LeaseState");
        LeaseExpiresAt = column("LeaseExpiresAt");
        LeaseHolder = column("LeaseHolder");
        LeaseTakeoverCount = column("LeaseTakeoverCount");
        LeaseColumnNames =
        [
            leaseColumns.Generation,
            leaseColumns.State,
            leaseColumns.ExpiresAt,
            leaseColumns.Holder,
            leaseColumns.TakeoverCount,
        ];
        LeaseColumns = [LeaseGeneration, LeaseState, LeaseExpiresAt, LeaseHolder, LeaseTakeoverCount];
        ActiveLease = ((short)RowLeaseState.Active).ToString(CultureInfo.InvariantCulture);
        LeaseLive = $"({LeaseState} = {ActiveLease} AND {LeaseExpiresAt} > {SqlDialectTokens.Now})";
        StatusName = column("StatusName");
        MessageId = column("MessageId");
        ExceptionInfo = column("ExceptionInfo");
        FailureFingerprint = column("FailureFingerprint");
        IsInboxRecord = column("IsInboxRecord");
        TenantPresent = column("TenantPresent");
        TenantId = column("TenantId");
        ContractIdentity = column("ContractIdentity");
        ContractVersion = column("ContractVersion");
        ConsumerIdentity = column("ConsumerIdentity");
        Generation = column("Generation");
        GenerationIncarnationId = column("GenerationIncarnationId");
        LifecycleId = column("LifecycleId");
        AttemptId = column("AttemptId");
        IsInboxOrphaned = column("IsInboxOrphaned");
        IsCurrentGeneration = column("IsCurrentGeneration");
        ReplayParentIncarnationId = column("ReplayParentIncarnationId");
        ReplayOperationId = column("ReplayOperationId");
        TerminalAt = column("TerminalAt");
        EffectiveExpiresAt = column("EffectiveExpiresAt");
        IsHeld = column("IsHeld");
        HeldAt = column("HeldAt");
        HeldBy = column("HeldBy");
        HoldReason = column("HoldReason");
        HoldOperationId = column("HoldOperationId");
        InboxRetentionSeconds = column("InboxRetentionSeconds");
        InboxKeyHash = column("InboxKeyHash");
        ConsumerIdentityHash = column("ConsumerIdentityHash");
        ConsumerKeyHash = column("ConsumerKeyHash");
        ChangedAt = column("ChangedAt");
        StampChange = $"{ChangedAt}={dialect.NextChangeMarker(ChangedAt)}";

        OperationId = column("OperationId");
        TargetKind = column("TargetKind");
        OperationType = column("OperationType");
        ExpectedStatus = column("ExpectedStatus");
        ExpectedDueAt = column("ExpectedDueAt");
        Actor = column("Actor");
        Reason = column("Reason");
        Outcome = column("Outcome");
        StorageId = column("StorageId");
        OutboxName = column("OutboxName");
        MessageName = column("MessageName");
        Lane = column("Lane");
        ChildStorageId = column("ChildStorageId");
        ChildGeneration = column("ChildGeneration");
        ChildIncarnationId = column("ChildIncarnationId");
        CreatedAt = column("CreatedAt");
        AuditId = column("AuditId");
    }

    /// <summary>
    /// Creates the names for the schema the feature's storage options configure, with lease tables that accept a holder
    /// of up to <paramref name="leaseHolderMaxLength" /> characters, the width the provider's schema gives the column.
    /// </summary>
    public static MessagingTables For(
        ISqlDialect dialect,
        IOptions<MessagingStorageOptions> storageOptions,
        int leaseHolderMaxLength
    )
    {
        return new MessagingTables(dialect, Argument.IsNotNull(storageOptions).Value.Schema, leaseHolderMaxLength);
    }

    public string Published { get; }

    public string Received { get; }

    /// <summary>The published table as a fenced-row table, whose row lease every dispatch attempt holds.</summary>
    public FencedRowTable<Guid> PublishedLease { get; }

    /// <summary>The received table as a fenced-row table, whose row lease every consume attempt holds.</summary>
    public FencedRowTable<Guid> ReceivedLease { get; }

    public string Receipts { get; }

    public string Audit { get; }

    /// <summary>The literal a boolean column compares equal to when set.</summary>
    public string True { get; }

    /// <summary>The literal a boolean column compares equal to when clear.</summary>
    public string False { get; }

    public string Id { get; }

    public string Version { get; }

    public string Name { get; }

    public string Content { get; }

    public string IntentType { get; }

    public string Retries { get; }

    public string InlineAttempts { get; }

    public string Added { get; }

    public string ExpiresAt { get; }

    public string NextRetryAt { get; }

    public string LeaseGeneration { get; }

    public string LeaseState { get; }

    public string LeaseExpiresAt { get; }

    public string LeaseHolder { get; }

    public string LeaseTakeoverCount { get; }

    /// <summary>The five lease columns, unquoted, in the order <see cref="RowLeaseReader" /> reads them.</summary>
    public IReadOnlyList<string> LeaseColumnNames { get; }

    /// <summary>The five lease columns, quoted, in the order <see cref="RowLeaseReader" /> reads them.</summary>
    public IReadOnlyList<string> LeaseColumns { get; }

    /// <summary>The literal of <see cref="RowLeaseState.Active" /> as the lease state column stores it.</summary>
    public string ActiveLease { get; }

    /// <summary>
    /// Whether a live attempt holds the row by the database clock (<see cref="SqlDialectTokens.Now" />): the predicate
    /// every operator and pickup path negates to leave a leased row to its holder.
    /// </summary>
    public string LeaseLive { get; }

    public string StatusName { get; }

    public string MessageId { get; }

    public string ExceptionInfo { get; }

    public string FailureFingerprint { get; }

    public string IsInboxRecord { get; }

    public string TenantPresent { get; }

    public string TenantId { get; }

    public string ContractIdentity { get; }

    public string ContractVersion { get; }

    public string ConsumerIdentity { get; }

    public string Generation { get; }

    public string GenerationIncarnationId { get; }

    public string LifecycleId { get; }

    public string AttemptId { get; }

    public string IsInboxOrphaned { get; }

    public string IsCurrentGeneration { get; }

    public string ReplayParentIncarnationId { get; }

    public string ReplayOperationId { get; }

    public string TerminalAt { get; }

    public string EffectiveExpiresAt { get; }

    public string IsHeld { get; }

    public string HeldAt { get; }

    public string HeldBy { get; }

    public string HoldReason { get; }

    public string HoldOperationId { get; }

    public string InboxRetentionSeconds { get; }

    /// <summary>The SHA-256 of an inbox root generation's identity, which its unique index is built on.</summary>
    public string InboxKeyHash { get; }

    /// <summary>
    /// The SHA-256 of a non-inbox received row's identity, which its unique index is built on so the key compares bytes
    /// rather than padded text.
    /// </summary>
    public string ConsumerIdentityHash { get; }

    /// <summary>
    /// The SHA-256 of a received row's consumer identity alone. Failure groups key on it rather than on the text, which
    /// SQL Server compares with trailing spaces padded away.
    /// </summary>
    public string ConsumerKeyHash { get; }

    /// <summary>
    /// The change marker of a published or received row: the database clock when a write last changed the row's
    /// visible state, kept strictly increasing per row. Change reads order and page by it.
    /// </summary>
    public string ChangedAt { get; }

    /// <summary>
    /// The assignment that moves a row's change marker forward, for the <c>SET</c> list of every statement that changes
    /// a row's visible state. It reads the database clock as <see cref="SqlDialectTokens.Now" />.
    /// </summary>
    public string StampChange { get; }

    public string OperationId { get; }

    public string TargetKind { get; }

    public string OperationType { get; }

    public string ExpectedStatus { get; }

    public string ExpectedDueAt { get; }

    public string Actor { get; }

    public string Reason { get; }

    public string Outcome { get; }

    public string StorageId { get; }

    public string OutboxName { get; }

    public string MessageName { get; }

    public string Lane { get; }

    public string ChildStorageId { get; }

    public string ChildGeneration { get; }

    public string ChildIncarnationId { get; }

    public string CreatedAt { get; }

    public string AuditId { get; }
}
