// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Configuration;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// The Messaging feature's schema contribution for SQL Server: the published and received message tables, the inbox operation receipt and audit history tables, and their indexes, as idempotent
/// steps the Headless schema runner applies.
/// </summary>
internal static class SqlServerMessagingSchemaContribution
{
    public const string Feature = "Messaging";
    public const string OutboxFeature = "MessagingOutbox";
    public const string TablesStepVersion = "1";
    public const string HistoryIndexesStepVersion = "2";

    /// <summary>Creates the contribution of the primary storage: the inbox, its history, and the published table.</summary>
    public static SchemaContribution Create(SqlServerOptions options, MessagingStorageOptions storageOptions)
    {
        var schema = storageOptions.Schema;
        var connectionString = options.ConnectionString;

        return new SchemaContribution(
            feature: Feature,
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => new SqlConnection(connectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(
                    TablesStepVersion,
                    "Create the published, received, inbox receipt, and inbox audit tables with their constraints and indexes.",
                    _InboxTablesSql(schema, options.OwnerColumnMaxLength)
                        + _PublishedTableSql(schema, options.OwnerColumnMaxLength)
                ),
                new SchemaStep(
                    HistoryIndexesStepVersion,
                    "Create the inbox receipt and audit history-selection indexes.",
                    _HistoryIndexesSql(schema)
                ),
            ]
        );
    }

    /// <summary>
    /// Creates the contribution of an additional outbox, whose database holds published rows only: the inbox, its
    /// history, and its readiness checks stay with the primary storage. It is its own feature because its steps differ
    /// from the primary's, and an outbox database never holds both.
    /// </summary>
    public static SchemaContribution CreateOutbox(SqlServerOptions options, MessagingStorageOptions storageOptions)
    {
        var schema = storageOptions.Schema;
        var connectionString = options.ConnectionString;

        return new SchemaContribution(
            feature: OutboxFeature,
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => new SqlConnection(connectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(
                    TablesStepVersion,
                    "Create the published table with its indexes.",
                    _PublishedTableSql(schema, options.OwnerColumnMaxLength)
                ),
            ]
        );
    }

    private static string _InboxTablesSql(string schema, int ownerColumnMaxLength)
    {
        // Constraint names are unique per schema, so the table name alone keeps them distinct; every
        // existence probe below is scoped to its table because the same name exists in every schema.
        const string receivedPrefix = "MessagingReceived";
        var received = SqlServerStorageTableNames.Received(schema);

        // Simplified SQL for Azure SQL Edge compatibility (no TEXTIMAGE_ON, simpler index options).

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            IF OBJECT_ID(N'{received}',N'U') IS NULL
            BEGIN
                CREATE TABLE {received}(
                    [Id] [uniqueidentifier] NOT NULL,
                    [Version] [nvarchar](20) NOT NULL,
                    [Name] [nvarchar](200) NOT NULL,
                    [Group] [nvarchar](200) NULL,
                    -- #19 — PERSISTED ISNULL collapses a NULL [Group] to '' so the unique index below
                    -- converges NULL-group redeliveries to one row, matching the PostgreSQL
                    -- COALESCE("Group", '') index (a plain nullable [Group] treats each NULL as distinct).
                    [GroupCoalesced] AS ISNULL([Group], N'') PERSISTED,
                    [Content] [nvarchar](max) NULL,
                    [IntentType] [smallint] NOT NULL,
                    [Retries] [int] NOT NULL,
                    [InlineAttempts] [int] NOT NULL CONSTRAINT [DF_{receivedPrefix}_InlineAttempts] DEFAULT 0,
                    [Added] [datetimeoffset](7) NOT NULL,
                    [ExpiresAt] [datetimeoffset](7) NULL,
                    [NextRetryAt] [datetimeoffset](7) NULL,
                    [LockedUntil] [datetimeoffset](7) NULL,
                    [Owner] [nvarchar]({ownerColumnMaxLength}) NULL,
                    [StatusName] [nvarchar](50) NOT NULL,
                    [MessageId] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [ExceptionInfo] [nvarchar](max) NULL,
                    [IsInboxRecord] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsInboxRecord] DEFAULT 0,
                    [TenantPresent] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_TenantPresent] DEFAULT 0,
                    [TenantId] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_TenantId] DEFAULT N'',
                    [ContractIdentity] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_ContractIdentity] DEFAULT N'',
                    [ContractVersion] [nvarchar](100) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_ContractVersion] DEFAULT N'',
                    [ConsumerIdentity] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_ConsumerIdentity] DEFAULT N'',
                    [Generation] [bigint] NOT NULL CONSTRAINT [DF_{receivedPrefix}_Generation] DEFAULT 0,
                    [GenerationIncarnationId] [uniqueidentifier] NULL,
                    [LifecycleId] [uniqueidentifier] NULL,
                    [AttemptId] [uniqueidentifier] NULL,
                    [IsInboxOrphaned] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsInboxOrphaned] DEFAULT 0,
                    [IsCurrentGeneration] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsCurrentGeneration] DEFAULT 1,
                    [ReplayParentIncarnationId] [uniqueidentifier] NULL,
                    [ReplayOperationId] [uniqueidentifier] NULL,
                    [TerminalAt] [datetimeoffset](7) NULL,
                    [EffectiveExpiresAt] [datetimeoffset](7) NULL,
                    [IsHeld] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsHeld] DEFAULT 0,
                    [HeldAt] [datetimeoffset](7) NULL,
                    [HeldBy] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NULL,
                    [HoldReason] [nvarchar](1000) NULL,
                    [HoldOperationId] [uniqueidentifier] NULL,
                    [InboxRetentionSeconds] [bigint] NOT NULL CONSTRAINT [DF_{receivedPrefix}_InboxRetentionSeconds] DEFAULT 2592000,
                    [TenantIdOrdinal] AS CONVERT(varbinary(400),[TenantId]) PERSISTED,
                    [MessageIdOrdinal] AS CONVERT(varbinary(400),[MessageId]) PERSISTED,
                    [ContractIdentityOrdinal] AS CONVERT(varbinary(400),[ContractIdentity]) PERSISTED,
                    [ContractVersionOrdinal] AS CONVERT(varbinary(200),[ContractVersion]) PERSISTED,
                    [ConsumerIdentityOrdinal] AS CONVERT(varbinary(400),[ConsumerIdentity]) PERSISTED,
                    [InboxKeyHash] [binary](32) NULL,
                    CONSTRAINT [PK_{receivedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_{receivedPrefix}_InboxIdentity'
                AND parent_object_id = OBJECT_ID(N'{received}'))
                EXEC(N'ALTER TABLE {received} ADD CONSTRAINT [CK_{receivedPrefix}_InboxIdentity] CHECK (
                    [IsInboxRecord]=0 OR (
                        [Generation]>=0 AND [InboxRetentionSeconds] BETWEEN 1 AND 2147483647 AND [GenerationIncarnationId] IS NOT NULL AND [InboxKeyHash] IS NOT NULL
                        AND LEN([MessageId]) BETWEEN 1 AND 200
                        AND LEN([ContractIdentity]) BETWEEN 1 AND 200
                        AND LEN([ContractVersion]) BETWEEN 1 AND 100
                        AND LEN([ConsumerIdentity]) BETWEEN 1 AND 200
                        AND (([TenantPresent]=0 AND DATALENGTH([TenantId])=0) OR ([TenantPresent]=1 AND LEN([TenantId]) BETWEEN 1 AND 200))
                    )
                )');

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_{receivedPrefix}_InboxLifecycle'
                AND parent_object_id = OBJECT_ID(N'{received}'))
                EXEC(N'ALTER TABLE {received} ADD CONSTRAINT [CK_{receivedPrefix}_InboxLifecycle] CHECK (
                    [IsInboxRecord]=0 OR ([LifecycleId] IS NOT NULL
                        AND ([ReplayParentIncarnationId] IS NOT NULL OR [LifecycleId]=[GenerationIncarnationId]))
                )');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_InboxLifecycleGeneration' AND object_id = OBJECT_ID(N'{received}'))
                EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_InboxLifecycleGeneration]
                    ON {received} ([LifecycleId],[Generation]) WHERE [IsInboxRecord]=1');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_InboxRootKey' AND object_id = OBJECT_ID(N'{received}'))
                EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_InboxRootKey] ON {received} ([InboxKeyHash] ASC) WHERE [IsInboxRecord]=1 AND [ReplayParentIncarnationId] IS NULL');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_NonInboxTransportIdentity' AND object_id = OBJECT_ID(N'{received}'))
                EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_NonInboxTransportIdentity]
                    ON {received} ([Version],[MessageId],[GroupCoalesced],[IntentType]) WHERE [IsInboxRecord]=0');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_GenerationIncarnationId' AND object_id = OBJECT_ID(N'{received}'))
                EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_GenerationIncarnationId] ON {received} ([GenerationIncarnationId] ASC) WHERE [GenerationIncarnationId] IS NOT NULL');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_InboxRetention' AND object_id = OBJECT_ID(N'{received}'))
                EXEC(N'CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_InboxRetention]
                    ON {received} ([EffectiveExpiresAt],[Id])
                    INCLUDE ([StatusName],[NextRetryAt],[IntentType],[IsInboxRecord],[GenerationIncarnationId]) WHERE [IsInboxRecord]=1 AND [IsHeld]=0');

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{received}'))
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_ExpiresAt_StatusName] ON {received} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{received}'))
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_ExpiresAt_StatusName] ON {received} ([ExpiresAt] ASC,[StatusName] ASC);

            -- #508 — ([StatusName],[Added]) serves BOTH the dashboard hourly-timeline query
            -- (WHERE StatusName=@p AND Added BETWEEN … — a StatusName seek + Added range scan) and the
            -- per-status COUNT_BIGs in GetStatisticsAsync via its [StatusName] prefix. The step creates
            -- the final schema directly; it does not carry migration DDL for superseded indexes.
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_StatusName_Added' AND object_id = OBJECT_ID(N'{received}'))
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_StatusName_Added] ON {received} ([StatusName] ASC, [Added] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_NextRetryAt' AND object_id = OBJECT_ID(N'{received}'))
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_NextRetryAt] ON {received} ([Version] ASC,[IntentType] ASC,[NextRetryAt] ASC) INCLUDE ([Retries],[LockedUntil]) WHERE [NextRetryAt] IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Owner_NotNull' AND object_id = OBJECT_ID(N'{received}'))
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Owner_NotNull] ON {received} ([Owner] ASC) WHERE [Owner] IS NOT NULL;

            IF OBJECT_ID(N'{schema}.MessagingInboxOperationReceipts',N'U') IS NULL
            BEGIN
                CREATE TABLE [{schema}].[MessagingInboxOperationReceipts](
                    [OperationId] [uniqueidentifier] NOT NULL,
                    [TargetKind] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_MessagingInboxOperationReceipts_TargetKind] DEFAULT N'Inbox',
                    [GenerationIncarnationId] [uniqueidentifier] NULL,
                    [OperationType] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [ExpectedStatus] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NULL,
                    [ExpectedDueAt] [datetimeoffset](7) NULL,
                    [Actor] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Reason] [nvarchar](1000) NOT NULL,
                    [Outcome] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [StorageId] [uniqueidentifier] NULL,
                    [MessageName] [nvarchar](200) NULL,
                    [MessageId] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NULL,
                    [Lane] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NULL,
                    [ChildStorageId] [uniqueidentifier] NULL,
                    [ChildGeneration] [bigint] NULL,
                    [ChildIncarnationId] [uniqueidentifier] NULL,
                    [CreatedAt] [datetimeoffset](7) NOT NULL,
                    CONSTRAINT [PK_MessagingInboxOperationReceipts] PRIMARY KEY CLUSTERED ([OperationId])
                );
            END;

            IF OBJECT_ID(N'{schema}.MessagingInboxAudit',N'U') IS NULL
            BEGIN
                CREATE TABLE [{schema}].[MessagingInboxAudit](
                    [AuditId] [uniqueidentifier] NOT NULL,
                    [OperationId] [uniqueidentifier] NOT NULL,
                    [TargetKind] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_MessagingInboxAudit_TargetKind] DEFAULT N'Inbox',
                    [GenerationIncarnationId] [uniqueidentifier] NULL,
                    [OperationType] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Actor] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Reason] [nvarchar](1000) NOT NULL,
                    [Outcome] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [CreatedAt] [datetimeoffset](7) NOT NULL,
                    CONSTRAINT [PK_MessagingInboxAudit] PRIMARY KEY CLUSTERED ([AuditId]),
                    CONSTRAINT [FK_MessagingInboxAudit_Operation] FOREIGN KEY ([OperationId])
                        REFERENCES [{schema}].[MessagingInboxOperationReceipts]([OperationId]) ON DELETE NO ACTION
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_MessagingInboxAudit_Incarnation_CreatedAt' AND object_id=OBJECT_ID(N'{schema}.MessagingInboxAudit'))
                CREATE NONCLUSTERED INDEX [IX_MessagingInboxAudit_Incarnation_CreatedAt]
                    ON [{schema}].[MessagingInboxAudit] ([GenerationIncarnationId],[CreatedAt]);

            -- The IF OBJECT_ID guards skip a table that already exists in another shape (created by an older
            -- binary or by hand), so the inbox contract the runtime depends on is asserted here: a missing
            -- constraint or column fails the step instead of the first inbox write.
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_{receivedPrefix}_InboxIdentity' AND parent_object_id=OBJECT_ID(N'{received}'))
               OR COL_LENGTH(N'{received}',N'LifecycleId') IS NULL
               OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_{receivedPrefix}_InboxLifecycle' AND parent_object_id=OBJECT_ID(N'{received}'))
               OR COL_LENGTH(N'{schema}.MessagingInboxOperationReceipts',N'ExpectedStatus') IS NULL
               OR COL_LENGTH(N'{schema}.MessagingInboxOperationReceipts',N'Outcome') IS NULL
               OR COL_LENGTH(N'{schema}.MessagingInboxOperationReceipts',N'TargetKind') IS NULL
               OR COL_LENGTH(N'{schema}.MessagingInboxOperationReceipts',N'ExpectedDueAt') IS NULL
               OR COL_LENGTH(N'{schema}.MessagingInboxAudit',N'TargetKind') IS NULL
                THROW 50004, N'Headless.Messaging inbox schema is incomplete: the lifecycle, retention or operation receipt contract is missing.', 1;
            """
        );
    }

    private static string _PublishedTableSql(string schema, int ownerColumnMaxLength)
    {
        const string publishedPrefix = "MessagingPublished";
        var published = SqlServerStorageTableNames.Published(schema);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            IF OBJECT_ID(N'{published}',N'U') IS NULL
            BEGIN
                CREATE TABLE {published}(
                    [Id] [uniqueidentifier] NOT NULL,
                    [Version] [nvarchar](20) NOT NULL,
                    [Name] [nvarchar](200) NOT NULL,
                    [Content] [nvarchar](max) NULL,
                    [IntentType] [smallint] NOT NULL,
                    [Retries] [int] NOT NULL,
                    [InlineAttempts] [int] NOT NULL CONSTRAINT [DF_{publishedPrefix}_InlineAttempts] DEFAULT 0,
                    [Added] [datetimeoffset](7) NOT NULL,
                    [ExpiresAt] [datetimeoffset](7) NULL,
                    [NextRetryAt] [datetimeoffset](7) NULL,
                    [LockedUntil] [datetimeoffset](7) NULL,
                    [Owner] [nvarchar]({ownerColumnMaxLength}) NULL,
                    [StatusName] [nvarchar](50) NOT NULL,
                    [MessageId] [nvarchar](200) NOT NULL,
                    CONSTRAINT [PK_{publishedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Version_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{published}'))
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_ExpiresAt_StatusName] ON {published} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{published}'))
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_ExpiresAt_StatusName] ON {published} ([ExpiresAt] ASC,[StatusName] ASC);

            -- #508 — see the received-table note above; create the final dashboard timeline/statistics index.
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_StatusName_Added' AND object_id = OBJECT_ID(N'{published}'))
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_StatusName_Added] ON {published} ([StatusName] ASC, [Added] ASC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Version_NextRetryAt' AND object_id = OBJECT_ID(N'{published}'))
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_NextRetryAt] ON {published} ([Version] ASC,[IntentType] ASC,[NextRetryAt] ASC) INCLUDE ([Retries],[LockedUntil]) WHERE [NextRetryAt] IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Owner_NotNull' AND object_id = OBJECT_ID(N'{published}'))
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Owner_NotNull] ON {published} ([Owner] ASC) WHERE [Owner] IS NOT NULL;

            """
        );
    }

    private static string _HistoryIndexesSql(string schema)
    {
        // The history tables grow without bound, so these selection and audit-reference indexes are their own step
        // rather than part of the table batch. The runner applies the step once, while the tables are still fresh.
        // ONLINE = ON is edition-dependent, so the builds stay offline.
        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_MessagingInboxOperationReceipts_Type_CreatedAt' AND object_id=OBJECT_ID(N'{schema}.MessagingInboxOperationReceipts'))
                CREATE NONCLUSTERED INDEX [IX_MessagingInboxOperationReceipts_Type_CreatedAt] ON [{schema}].[MessagingInboxOperationReceipts] ([OperationType],[CreatedAt]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_MessagingInboxAudit_Type_CreatedAt' AND object_id=OBJECT_ID(N'{schema}.MessagingInboxAudit'))
                CREATE NONCLUSTERED INDEX [IX_MessagingInboxAudit_Type_CreatedAt] ON [{schema}].[MessagingInboxAudit] ([OperationType],[CreatedAt]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_MessagingInboxAudit_Operation' AND object_id=OBJECT_ID(N'{schema}.MessagingInboxAudit'))
                CREATE NONCLUSTERED INDEX [IX_MessagingInboxAudit_Operation] ON [{schema}].[MessagingInboxAudit] ([OperationId]);
            """;
    }
}
