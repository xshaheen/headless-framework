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
    /// table-valued parameter type if they do not already exist. Each DDL block is guarded by
    /// an <c>IF NOT EXISTS</c> check inside a <c>TRY/CATCH</c> that swallows only duplicate-object
    /// (2714) and PK-violation (2627) errors so concurrent multi-replica startups converge safely.
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
        // its own IF NOT EXISTS guard plus a narrow TRY/CATCH that swallows only the
        // duplicate-object (2714) and PK-violation (2627) errors raised under a TOCTOU race
        // between concurrent startups (multi-replica rollouts). A wrapping transaction would
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
        var batchSql = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{schema}]');
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

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
                        [Added] [datetime2](7) NOT NULL,
                        [ExpiresAt] [datetime2](7) NULL,
                        [NextRetryAt] [datetime2](7) NULL,
                        [LockedUntil] [datetime2](7) NULL,
                        [Owner] [nvarchar]({options.Value.OwnerColumnMaxLength}) NULL,
                        [StatusName] [nvarchar](50) NOT NULL,
                        [MessageId] [nvarchar](200) NOT NULL,
                        [ExceptionInfo] [nvarchar](max) NULL,
                        CONSTRAINT [PK_{receivedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );

                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            -- #19 — unique index on the GroupCoalesced computed column (not the nullable [Group]) so the
            -- schema-level uniqueness guarantee for NULL groups matches PostgreSQL; raw INSERTs that bypass
            -- the MERGE can no longer accumulate duplicate NULL-group rows.
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_MessageId_GroupCoalesced_IntentType' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_MessageId_GroupCoalesced_IntentType] ON {GetReceivedTableName()} ([Version] ASC, [MessageId] ASC, [GroupCoalesced] ASC, [IntentType] ASC);
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

            -- #8 — standalone StatusName index so GetStatisticsAsync per-status COUNT_BIGs do an index
            -- scan instead of a full scan on large tables (composite indexes lead with ExpiresAt/Version).
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_StatusName' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_StatusName] ON {GetReceivedTableName()} ([StatusName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{receivedPrefix}_Version_NextRetryAt' AND object_id = OBJECT_ID(N'{GetReceivedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_NextRetryAt] ON {GetReceivedTableName()} ([Version] ASC,[NextRetryAt] ASC) INCLUDE ([Retries],[LockedUntil]) WHERE [NextRetryAt] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF COL_LENGTH(N'{GetReceivedTableName()}', N'Owner') IS NULL
                    ALTER TABLE {GetReceivedTableName()} ADD [Owner] [nvarchar]({options.Value.OwnerColumnMaxLength}) NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714, 2705) THROW;
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
                        [Added] [datetime2](7) NOT NULL,
                        [ExpiresAt] [datetime2](7) NULL,
                        [NextRetryAt] [datetime2](7) NULL,
                        [LockedUntil] [datetime2](7) NULL,
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

            -- #8 — see the received-table note above; standalone StatusName index for dashboard statistics.
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_StatusName' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_StatusName] ON {GetPublishedTableName()} ([StatusName] ASC);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Version_NextRetryAt' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_NextRetryAt] ON {GetPublishedTableName()} ([Version] ASC,[NextRetryAt] ASC) INCLUDE ([Retries],[LockedUntil]) WHERE [NextRetryAt] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            BEGIN TRY
                IF COL_LENGTH(N'{GetPublishedTableName()}', N'Owner') IS NULL
                    ALTER TABLE {GetPublishedTableName()} ADD [Owner] [nvarchar]({options.Value.OwnerColumnMaxLength}) NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714, 2705) THROW;
            END CATCH;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{publishedPrefix}_Owner_NotNull' AND object_id = OBJECT_ID(N'{GetPublishedTableName()}'))
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Owner_NotNull] ON {GetPublishedTableName()} ([Owner] ASC) WHERE [Owner] IS NOT NULL;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (1913, 2714) THROW;
            END CATCH;

            """
        );

        return batchSql;
    }
}
