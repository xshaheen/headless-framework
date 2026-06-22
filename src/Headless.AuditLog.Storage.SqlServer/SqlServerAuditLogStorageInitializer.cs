// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerAuditLogStorageInitializer(
    IOptions<SqlServerAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions
) : HostedInitializer
{
    protected override bool RunOnStartup => storageOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection);
        command.CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(AuditLogStorageOptions options) => $"[{options.Schema}].[{options.TableName}]";

    internal static string ObjectName(AuditLogStorageOptions options) => $"{options.Schema}.{options.TableName}";

    private static string _CreateScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var objectName = ObjectName(options);
        var jsonColumnType = (options.JsonColumnType ?? AuditLogJsonColumnType.NvarcharMax).ToSqlFragment();
        var createdAtColumnType = string.IsNullOrWhiteSpace(options.CreatedAtColumnType)
            ? "datetime2"
            : options.CreatedAtColumnType;

        // Serialize concurrent-startup DDL across replicas with a session-scoped advisory lock.
        // Without this, multiple hosts racing CREATE INDEX on the same table deadlock on schema-mod
        // locks (error 1205). The outer TRY/CATCH below guarantees the lock is released on the
        // failure path; connection-close auto-release is a backstop, not the primary mechanism.
        var acquireLock = $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = N'headless_audit_init:{options.Schema}.{options.TableName}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.AuditLog: failed to acquire init lock on the audit_log schema. Another initializer may be holding it.', 1;
            """;

        var createSchema = $"""
              BEGIN TRY
                  IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{options.Schema}')
                      EXEC(N'CREATE SCHEMA [{options.Schema}]');
              END TRY
              BEGIN CATCH
                  IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
              END CATCH;
            """;

        var createTable = $"""
            BEGIN TRY
                IF OBJECT_ID(N'{objectName}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {table} (
                        [Id] bigint IDENTITY(1,1) NOT NULL,
                        [CreatedAt] {createdAtColumnType} NOT NULL,
                        [UserId] nvarchar({AuditLogFieldLimits.UserId}) NULL,
                        [AccountId] nvarchar({AuditLogFieldLimits.AccountId}) NULL,
                        [TenantId] nvarchar({AuditLogFieldLimits.TenantId}) NULL,
                        [IpAddress] nvarchar({AuditLogFieldLimits.IpAddress}) NULL,
                        [UserAgent] nvarchar({AuditLogFieldLimits.UserAgent}) NULL,
                        [CorrelationId] nvarchar({AuditLogFieldLimits.CorrelationId}) NULL,
                        [Action] nvarchar({AuditLogFieldLimits.Action}) NOT NULL,
                        [ChangeType] int NULL,
                        [EntityType] nvarchar({AuditLogFieldLimits.EntityType}) NULL,
                        [EntityId] nvarchar({AuditLogFieldLimits.EntityId}) NULL,
                        [OldValues] {jsonColumnType} NULL,
                        [NewValues] {jsonColumnType} NULL,
                        [ChangedFields] {jsonColumnType} NULL,
                        [Success] bit NOT NULL,
                        [ErrorCode] nvarchar({AuditLogFieldLimits.ErrorCode}) NULL,
                        CONSTRAINT [PK_{options.TableName}] PRIMARY KEY CLUSTERED ([CreatedAt] ASC, [Id] ASC)
                    );
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;

        // Index creation runs unconditionally on every startup — each CREATE INDEX is gated by its
        // own existence check so a previous partial-failure run that committed the table but missed
        // an index self-heals on the next start. This matches the PG initializer's per-statement
        // `CREATE INDEX IF NOT EXISTS` behavior.
        var createIndexes = string.Join(
            "\n",
            new[]
            {
                _IndexStatement("ix_audit_log_tenant_time", table, objectName, "[TenantId] ASC, [CreatedAt] ASC"),
                _IndexStatement(
                    "ix_audit_log_tenant_action_time",
                    table,
                    objectName,
                    "[TenantId] ASC, [Action] ASC, [CreatedAt] ASC"
                ),
                _IndexStatement(
                    "ix_audit_log_tenant_entity_time",
                    table,
                    objectName,
                    "[TenantId] ASC, [EntityType] ASC, [EntityId] ASC, [CreatedAt] ASC"
                ),
                _IndexStatement(
                    "ix_audit_log_tenant_actor_time",
                    table,
                    objectName,
                    "[TenantId] ASC, [UserId] ASC, [CreatedAt] ASC"
                ),
                _IndexStatement("ix_audit_log_correlation", table, objectName, "[CorrelationId] ASC"),
            }
        );

        // Release the advisory lock on every path — success AND failure. Wrapping the DDL body in
        // an outer TRY/CATCH guarantees the release runs before the connection returns to the pool;
        // a Session-scoped applock that leaks past the throw would otherwise persist and starve the
        // next replica's sp_getapplock until the connection is physically reset.
        var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
        var releaseLock = $"EXEC sp_releaseapplock @Resource = N'{lockResource}', @LockOwner = N'Session';";

        // Wrap the DDL body in BEGIN TRAN / COMMIT TRAN so a mid-script failure (constraint-violation,
        // deadlock victim, network drop) cannot leave the schema half-initialized. CREATE TABLE /
        // CREATE INDEX inside an explicit transaction are supported by SQL Server. Inner BEGIN TRY
        // swallow-lists keep soft errors (2714, 1913, 2759) from dooming the outer transaction.
        return $"""
            {acquireLock}

            BEGIN TRY
                BEGIN TRAN;

                {createSchema}

                {createTable}

                {createIndexes}

                COMMIT TRAN;

                {releaseLock}
            END TRY
            BEGIN CATCH
                -- Roll back the outer transaction on any failure path. XACT_STATE() returns 1 for
                -- an active transaction, -1 for a doomed-but-still-open transaction (both require
                -- ROLLBACK), 0 for no active transaction. Skip the ROLLBACK when 0 to avoid raising
                -- a secondary error that masks the original DDL exception.
                IF XACT_STATE() <> 0 ROLLBACK TRAN;

                -- Wrap the conditional release in its own TRY/CATCH so a release-side error
                -- (e.g., transient lock-state inconsistency) does NOT terminate the outer CATCH
                -- before THROW runs. Without this guard, sp_releaseapplock raising would replace
                -- the original DDL exception with the release error and hide root cause. The
                -- session-scoped applock is auto-released on connection-pool reset as backstop.
                BEGIN TRY
                    IF APPLOCK_MODE('public', N'{lockResource}', 'Session') <> 'NoLock'
                        {releaseLock}
                END TRY
                BEGIN CATCH
                    -- intentional: swallow release-side error so original DDL exception survives
                END CATCH;
                THROW;
            END CATCH;
            """;
    }

    private static string _IndexStatement(string indexName, string table, string objectName, string columns) =>
        $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexName}' AND object_id = OBJECT_ID(N'{objectName}'))
                    CREATE NONCLUSTERED INDEX [{indexName}] ON {table} ({columns});
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;
}
