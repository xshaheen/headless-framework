// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IStorageInitializer"/> for database schema setup.
/// Creates required tables (published, received) and indexes on first run.
/// </summary>
internal sealed class SqlServerStorageInitializer(
    ILogger<SqlServerStorageInitializer> logger,
    IOptions<SqlServerOptions> options,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    /// <summary>
    /// Returns the fully-qualified SQL Server table name for published outbox messages,
    /// in the form <c>schema.Published</c>.
    /// </summary>
    public string GetPublishedTableName()
    {
        return $"{options.Value.Schema}.Published";
    }

    /// <summary>
    /// Returns the fully-qualified SQL Server table name for received outbox messages,
    /// in the form <c>schema.Received</c>.
    /// </summary>
    public string GetReceivedTableName()
    {
        return $"{options.Value.Schema}.Received";
    }

    /// <summary>
    /// Creates the messaging schema, tables, indexes, and the <c>HeadlessMessagingIdList</c>
    /// table-valued parameter type if they do not already exist. Concurrent initializers are serialized
    /// with a session-scoped <c>sp_getapplock</c>; each DDL block remains independently idempotent so a
    /// later initialization can repair a partially completed schema.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var sql = _CreateDbTablesScript(options.Value.Schema);
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // No wrapping transaction: each idempotent block in the script is already protected by
        // its own IF NOT EXISTS guard plus a narrow TRY/CATCH. The session-scoped application lock
        // serializes the full repair sequence across concurrent startups. A wrapping transaction would
        // interact poorly with sessions that have SET XACT_ABORT ON — statement-level errors
        // doom the transaction (XACT_STATE = -1), the inner CATCH swallows the error, but the
        // outer COMMIT then fails with 3930, masking the real cause. A mid-script abort
        // (network drop, transient timeout) without the transaction just leaves a partially
        // initialized schema that the next initialize pass re-creates piece-by-piece because
        // every block is guarded by IF NOT EXISTS.
        await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        logger.LogEnsuringTablesCreated();
    }

    private string _CreateDbTablesScript(string schema)
    {
        // Use underscore instead of period in constraint/index names for Azure SQL Edge compatibility
        var receivedPrefix = $"{schema}_Received";
        var publishedPrefix = $"{schema}_Published";

        // Simplified SQL for Azure SQL Edge compatibility (no TEXTIMAGE_ON, simpler index options).
        // Each idempotent block is wrapped in BEGIN TRY ... BEGIN CATCH to absorb the narrow set of
        // duplicate-object/duplicate-key errors that fire only under a TOCTOU race between concurrent
        // initializers (e.g., simultaneous pod startup). Any other error is rethrown.
        //   2714 — "There is already an object named '...' in the database." (schema/table races)
        //   1913 — index already exists (index creation races)
        //   2627 — "Violation of PRIMARY KEY constraint." (lock-row INSERT races)
        var lockResource = $"headless_messaging_init:{schema}";
        var batchSql = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = N'{lockResource}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Messaging: failed to acquire init lock on the messaging schema. Another initializer may be holding it.', 1;

            BEGIN TRY
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{schema}]');
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            IF OBJECT_ID(N'{schema}.SchemaState',N'U') IS NOT NULL
                EXEC(N'
                    IF EXISTS (SELECT 1 FROM [{schema}].[SchemaState] WHERE [Component]=N''inbox'' AND [SchemaVersion] > 3)
                        THROW 50003, N''Headless.Messaging inbox schema is newer than supported version 3. Upgrade the application before starting this binary.'', 1;
                    DELETE FROM [{schema}].[SchemaState] WHERE [Component]=N''inbox'';
                ');

            BEGIN TRY
                IF TYPE_ID(N'{schema}.HeadlessMessagingIdList') IS NULL
                    CREATE TYPE [{schema}].[HeadlessMessagingIdList] AS TABLE ([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            BEGIN TRY
                IF TYPE_ID(N'{schema}.HeadlessMessagingOwnerList') IS NULL
                    CREATE TYPE [{schema}].[HeadlessMessagingOwnerList] AS TABLE ([Owner] [nvarchar]({options.Value.OwnerColumnMaxLength}) NOT NULL PRIMARY KEY);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            BEGIN TRY
                IF TYPE_ID(N'{schema}.HeadlessMessagingPoisonMessageList') IS NULL
                    CREATE TYPE [{schema}].[HeadlessMessagingPoisonMessageList] AS TABLE (
                        [Id] [uniqueidentifier] NOT NULL PRIMARY KEY,
                        [ExceptionInfo] [nvarchar](max) NOT NULL
                    );
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{GetReceivedTableName()}',N'U') IS NULL
                BEGIN
                    CREATE TABLE {GetReceivedTableName()}(
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
                        [Owner] [nvarchar]({options.Value.OwnerColumnMaxLength}) NULL,
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
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            -- A nonempty baseline table has no trustworthy stable consumer or contract identity.
            -- Stop startup rather than synthesizing identity. Empty/partial schemas are repaired in place.
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'GenerationIncarnationId') IS NULL
               AND EXISTS (SELECT TOP (1) 1 FROM {GetReceivedTableName()})
                THROW 50001, N'Headless.Messaging cannot upgrade a nonempty legacy Received table without stable inbox identity. Export or reset it, then restart.', 1;

            IF COL_LENGTH(N'{GetReceivedTableName()}', N'IsInboxRecord') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [IsInboxRecord] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsInboxRecord] DEFAULT 0;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'TenantPresent') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [TenantPresent] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_TenantPresent] DEFAULT 0;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'TenantId') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [TenantId] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_TenantId] DEFAULT N'';
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ContractIdentity') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [ContractIdentity] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_ContractIdentity] DEFAULT N'';
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ContractVersion') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [ContractVersion] [nvarchar](100) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_ContractVersion] DEFAULT N'';
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ConsumerIdentity') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [ConsumerIdentity] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{receivedPrefix}_ConsumerIdentity] DEFAULT N'';
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'Generation') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [Generation] [bigint] NOT NULL CONSTRAINT [DF_{receivedPrefix}_Generation] DEFAULT 0;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'GenerationIncarnationId') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [GenerationIncarnationId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'AttemptId') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [AttemptId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'IsInboxOrphaned') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [IsInboxOrphaned] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsInboxOrphaned] DEFAULT 0;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'IsCurrentGeneration') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [IsCurrentGeneration] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsCurrentGeneration] DEFAULT 1;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ReplayParentIncarnationId') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [ReplayParentIncarnationId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ReplayOperationId') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [ReplayOperationId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'TerminalAt') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [TerminalAt] [datetimeoffset](7) NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'EffectiveExpiresAt') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [EffectiveExpiresAt] [datetimeoffset](7) NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'IsHeld') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [IsHeld] [bit] NOT NULL CONSTRAINT [DF_{receivedPrefix}_IsHeld] DEFAULT 0;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'HeldAt') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [HeldAt] [datetimeoffset](7) NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'HeldBy') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [HeldBy] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'HoldReason') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [HoldReason] [nvarchar](1000) NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'HoldOperationId') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [HoldOperationId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'InboxRetentionSeconds') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [InboxRetentionSeconds] [bigint] NOT NULL CONSTRAINT [DF_{receivedPrefix}_InboxRetentionSeconds] DEFAULT 2592000;
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'TenantIdOrdinal') IS NULL
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD [TenantIdOrdinal] AS CONVERT(varbinary(400),[TenantId]) PERSISTED');
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'MessageIdOrdinal') IS NULL
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD [MessageIdOrdinal] AS CONVERT(varbinary(400),[MessageId]) PERSISTED');
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ContractIdentityOrdinal') IS NULL
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD [ContractIdentityOrdinal] AS CONVERT(varbinary(400),[ContractIdentity]) PERSISTED');
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ContractVersionOrdinal') IS NULL
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD [ContractVersionOrdinal] AS CONVERT(varbinary(200),[ContractVersion]) PERSISTED');
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'ConsumerIdentityOrdinal') IS NULL
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD [ConsumerIdentityOrdinal] AS CONVERT(varbinary(400),[ConsumerIdentity]) PERSISTED');
            IF COL_LENGTH(N'{GetReceivedTableName()}', N'InboxKeyHash') IS NULL
                ALTER TABLE {GetReceivedTableName()} ADD [InboxKeyHash] [binary](32) NULL;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_MessageId_GroupCoalesced_IntentType' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                DROP INDEX [IX_{receivedPrefix}_Version_MessageId_GroupCoalesced_IntentType] ON {GetReceivedTableName()};

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_{receivedPrefix}_InboxIdentity')
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD CONSTRAINT [CK_{receivedPrefix}_InboxIdentity] CHECK (
                    [IsInboxRecord]=0 OR (
                        [Generation]>=0 AND [InboxRetentionSeconds] BETWEEN 1 AND 2147483647 AND [GenerationIncarnationId] IS NOT NULL AND [InboxKeyHash] IS NOT NULL
                        AND LEN([MessageId]) BETWEEN 1 AND 200
                        AND LEN([ContractIdentity]) BETWEEN 1 AND 200
                        AND LEN([ContractVersion]) BETWEEN 1 AND 100
                        AND LEN([ConsumerIdentity]) BETWEEN 1 AND 200
                        AND (([TenantPresent]=0 AND DATALENGTH([TenantId])=0) OR ([TenantPresent]=1 AND LEN([TenantId]) BETWEEN 1 AND 200))
                    )
                )');

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_{receivedPrefix}_InboxRetentionV3')
                EXEC(N'ALTER TABLE {GetReceivedTableName()} ADD CONSTRAINT [CK_{receivedPrefix}_InboxRetentionV3] CHECK (
                    [IsInboxRecord]=0 OR [InboxRetentionSeconds] BETWEEN 1 AND 2147483647
                )');

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_InboxKey' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_InboxKey] ON {GetReceivedTableName()} ([InboxKeyHash] ASC) WHERE [IsInboxRecord]=1');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_NonInboxTransportIdentity' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_NonInboxTransportIdentity]
                        ON {GetReceivedTableName()} ([Version],[MessageId],[GroupCoalesced],[IntentType]) WHERE [IsInboxRecord]=0');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_{receivedPrefix}_GenerationIncarnationId' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX [UX_{receivedPrefix}_GenerationIncarnationId] ON {GetReceivedTableName()} ([GenerationIncarnationId] ASC) WHERE [GenerationIncarnationId] IS NOT NULL');
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([ExpiresAt] ASC,[StatusName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            -- #508 — ([StatusName],[Added]) serves BOTH the dashboard hourly-timeline query
            -- (WHERE StatusName=@p AND Added BETWEEN … — a StatusName seek + Added range scan) and the
            -- per-status COUNT_BIGs in GetStatisticsAsync via its [StatusName] prefix. The initializer
            -- creates the final schema directly; it does not carry migration DDL for superseded indexes.
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_StatusName_Added' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_StatusName_Added] ON {GetReceivedTableName()} ([StatusName] ASC, [Added] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_NextRetryAt' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_NextRetryAt] ON {GetReceivedTableName()} ([Version] ASC,[IntentType] ASC,[NextRetryAt] ASC) INCLUDE ([Retries],[LockedUntil]) WHERE [NextRetryAt] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Owner_NotNull' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Owner_NotNull] ON {GetReceivedTableName()} ([Owner] ASC) WHERE [Owner] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{GetPublishedTableName()}',N'U') IS NULL
                BEGIN
                    CREATE TABLE {GetPublishedTableName()}(
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
                        [Owner] [nvarchar]({options.Value.OwnerColumnMaxLength}) NULL,
                        [StatusName] [nvarchar](50) NOT NULL,
                        [MessageId] [nvarchar](200) NOT NULL,
                        CONSTRAINT [PK_{publishedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );

                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Version_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_ExpiresAt_StatusName' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([ExpiresAt] ASC,[StatusName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            -- #508 — see the received-table note above; create the final dashboard timeline/statistics index.
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_StatusName_Added' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_StatusName_Added] ON {GetPublishedTableName()} ([StatusName] ASC, [Added] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Version_NextRetryAt' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_NextRetryAt] ON {GetPublishedTableName()} ([Version] ASC,[IntentType] ASC,[NextRetryAt] ASC) INCLUDE ([Retries],[LockedUntil]) WHERE [NextRetryAt] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Owner_NotNull' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Owner_NotNull] ON {GetPublishedTableName()} ([Owner] ASC) WHERE [Owner] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            IF OBJECT_ID(N'{schema}.InboxOperationReceipts',N'U') IS NULL
            BEGIN
                CREATE TABLE [{schema}].[InboxOperationReceipts](
                    [OperationId] [uniqueidentifier] NOT NULL,
                    [GenerationIncarnationId] [uniqueidentifier] NOT NULL,
                    [OperationType] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [ExpectedStatus] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Actor] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Reason] [nvarchar](1000) NOT NULL,
                    [Outcome] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [StorageId] [uniqueidentifier] NULL,
                    [ChildStorageId] [uniqueidentifier] NULL,
                    [ChildGeneration] [bigint] NULL,
                    [ChildIncarnationId] [uniqueidentifier] NULL,
                    [CreatedAt] [datetimeoffset](7) NOT NULL,
                    CONSTRAINT [PK_{schema}_InboxOperationReceipts] PRIMARY KEY CLUSTERED ([OperationId])
                );
            END;

            IF COL_LENGTH(N'{schema}.InboxOperationReceipts', N'Outcome') IS NULL
                ALTER TABLE [{schema}].[InboxOperationReceipts] ADD [Outcome] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{schema}_InboxOperationReceipts_Outcome] DEFAULT N'StateConflict';
            IF COL_LENGTH(N'{schema}.InboxOperationReceipts', N'ExpectedStatus') IS NULL
                ALTER TABLE [{schema}].[InboxOperationReceipts] ADD [ExpectedStatus] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_{schema}_InboxOperationReceipts_ExpectedStatus] DEFAULT N'Failed';
            IF COL_LENGTH(N'{schema}.InboxOperationReceipts', N'StorageId') IS NULL
                ALTER TABLE [{schema}].[InboxOperationReceipts] ADD [StorageId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{schema}.InboxOperationReceipts', N'ChildStorageId') IS NULL
                ALTER TABLE [{schema}].[InboxOperationReceipts] ADD [ChildStorageId] [uniqueidentifier] NULL;
            IF COL_LENGTH(N'{schema}.InboxOperationReceipts', N'ChildGeneration') IS NULL
                ALTER TABLE [{schema}].[InboxOperationReceipts] ADD [ChildGeneration] [bigint] NULL;
            IF COL_LENGTH(N'{schema}.InboxOperationReceipts', N'ChildIncarnationId') IS NULL
                ALTER TABLE [{schema}].[InboxOperationReceipts] ADD [ChildIncarnationId] [uniqueidentifier] NULL;

            IF OBJECT_ID(N'{schema}.InboxAudit',N'U') IS NULL
            BEGIN
                CREATE TABLE [{schema}].[InboxAudit](
                    [AuditId] [uniqueidentifier] NOT NULL,
                    [OperationId] [uniqueidentifier] NOT NULL,
                    [GenerationIncarnationId] [uniqueidentifier] NOT NULL,
                    [OperationType] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Actor] [nvarchar](200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Reason] [nvarchar](1000) NOT NULL,
                    [Outcome] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [CreatedAt] [datetimeoffset](7) NOT NULL,
                    CONSTRAINT [PK_{schema}_InboxAudit] PRIMARY KEY CLUSTERED ([AuditId]),
                    CONSTRAINT [FK_{schema}_InboxAudit_Operation] FOREIGN KEY ([OperationId])
                        REFERENCES [{schema}].[InboxOperationReceipts]([OperationId]) ON DELETE NO ACTION
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_{schema}_InboxAudit_Incarnation_CreatedAt' AND object_id=OBJECT_ID(N'{schema}.InboxAudit'))
                CREATE NONCLUSTERED INDEX [IX_{schema}_InboxAudit_Incarnation_CreatedAt]
                    ON [{schema}].[InboxAudit] ([GenerationIncarnationId],[CreatedAt]);

            IF OBJECT_ID(N'{schema}.SchemaState',N'U') IS NULL
            BEGIN
                CREATE TABLE [{schema}].[SchemaState](
                    [Component] [nvarchar](50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [SchemaVersion] [int] NOT NULL,
                    [ReadyAt] [datetimeoffset](7) NOT NULL,
                    CONSTRAINT [PK_{schema}_SchemaState] PRIMARY KEY CLUSTERED ([Component])
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_{receivedPrefix}_InboxKey' AND object_id=OBJECT_ID(N'{GetReceivedTableName()}'))
                THROW 50002, N'Headless.Messaging inbox schema is incomplete: the final inbox key index is missing.', 1;

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_{receivedPrefix}_InboxRetentionV3')
               OR COL_LENGTH(N'{schema}.InboxOperationReceipts',N'ExpectedStatus') IS NULL
               OR COL_LENGTH(N'{schema}.InboxOperationReceipts',N'Outcome') IS NULL
                THROW 50004, N'Headless.Messaging inbox schema is incomplete: the v3 retention or operation receipt contract is missing.', 1;

            MERGE [{schema}].[SchemaState] WITH (HOLDLOCK) AS target
            USING (SELECT N'inbox' AS [Component], 3 AS [SchemaVersion], SYSDATETIMEOFFSET() AS [ReadyAt]) AS source
            ON target.[Component]=source.[Component]
            WHEN MATCHED THEN UPDATE SET [SchemaVersion]=source.[SchemaVersion],[ReadyAt]=source.[ReadyAt]
            WHEN NOT MATCHED THEN INSERT ([Component],[SchemaVersion],[ReadyAt]) VALUES (source.[Component],source.[SchemaVersion],source.[ReadyAt]);

                EXEC sp_releaseapplock @Resource = N'{lockResource}', @LockOwner = N'Session';
            END TRY
            BEGIN CATCH
                -- Keep release failures from replacing the original DDL exception. Closing the
                -- connection remains a backstop for this session-scoped lock.
                BEGIN TRY
                    IF APPLOCK_MODE('public', N'{lockResource}', 'Session') <> 'NoLock'
                        EXEC sp_releaseapplock @Resource = N'{lockResource}', @LockOwner = N'Session';
                END TRY
                BEGIN CATCH
                    -- intentional: preserve the original initializer failure
                END CATCH;
                THROW;
            END CATCH;

            """
        );

        return batchSql;
    }
}
