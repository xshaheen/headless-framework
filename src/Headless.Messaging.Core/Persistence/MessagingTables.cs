// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
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
    public MessagingTables(ISqlDialect dialect, string schema)
    {
        Argument.IsNotNull(dialect);
        Argument.IsNotNullOrWhiteSpace(schema);

        Published = dialect.Qualify(schema, dialect.Name("MessagingPublished"));
        Received = dialect.Qualify(schema, dialect.Name("MessagingReceived"));
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
        LockedUntil = column("LockedUntil");
        Owner = column("Owner");
        StatusName = column("StatusName");
        MessageId = column("MessageId");
        ExceptionInfo = column("ExceptionInfo");
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

        OperationId = column("OperationId");
        TargetKind = column("TargetKind");
        OperationType = column("OperationType");
        ExpectedStatus = column("ExpectedStatus");
        ExpectedDueAt = column("ExpectedDueAt");
        Actor = column("Actor");
        Reason = column("Reason");
        Outcome = column("Outcome");
        StorageId = column("StorageId");
        MessageName = column("MessageName");
        Lane = column("Lane");
        ChildStorageId = column("ChildStorageId");
        ChildGeneration = column("ChildGeneration");
        ChildIncarnationId = column("ChildIncarnationId");
        CreatedAt = column("CreatedAt");
        AuditId = column("AuditId");
    }

    /// <summary>Creates the names for the schema the feature's storage options configure.</summary>
    public static MessagingTables For(ISqlDialect dialect, IOptions<MessagingStorageOptions> storageOptions)
    {
        return new MessagingTables(dialect, Argument.IsNotNull(storageOptions).Value.Schema);
    }

    public string Published { get; }

    public string Received { get; }

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

    public string LockedUntil { get; }

    public string Owner { get; }

    public string StatusName { get; }

    public string MessageId { get; }

    public string ExceptionInfo { get; }

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

    public string OperationId { get; }

    public string TargetKind { get; }

    public string OperationType { get; }

    public string ExpectedStatus { get; }

    public string ExpectedDueAt { get; }

    public string Actor { get; }

    public string Reason { get; }

    public string Outcome { get; }

    public string StorageId { get; }

    public string MessageName { get; }

    public string Lane { get; }

    public string ChildStorageId { get; }

    public string ChildGeneration { get; }

    public string ChildIncarnationId { get; }

    public string CreatedAt { get; }

    public string AuditId { get; }
}
