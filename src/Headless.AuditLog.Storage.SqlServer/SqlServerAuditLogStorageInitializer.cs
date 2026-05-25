// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerAuditLogStorageInitializer(
    IOptions<SqlServerAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions
) : IHostedLifecycleService, IInitializer
{
    private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // Cancel any in-flight waiters on the previous TCS before swapping. Without this, a caller
        // that awaited WaitForInitializationAsync before a host restart would hang on an abandoned
        // promise that nobody will ever resolve.
        _completion.TrySetCanceled(cancellationToken);
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            IsInitialized = true;
            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            _completion.TrySetException(ex);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection)
        {
            CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds,
        };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(AuditLogStorageOptions options) => $"[{options.Schema}].[{options.TableName}]";

    internal static string ObjectName(AuditLogStorageOptions options) => $"{options.Schema}.{options.TableName}";

    private static string _CreateScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var objectName = ObjectName(options);
        var jsonColumnType = (options.JsonColumnType ?? AuditLogJsonColumnType.NvarcharMax).ToSqlFragment();

        // Serialize concurrent-startup DDL across replicas with a session-scoped advisory lock.
        // Without this, multiple hosts racing CREATE INDEX on the same table deadlock on schema-mod
        // locks (error 1205) — and once any host crashes mid-script, partial state would persist.
        // sp_getapplock with Session scope releases automatically when the connection closes.
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
                        [CreatedAt] datetime2 NOT NULL,
                        [UserId] nvarchar(128) NULL,
                        [AccountId] nvarchar(128) NULL,
                        [TenantId] nvarchar(128) NULL,
                        [IpAddress] nvarchar(45) NULL,
                        [UserAgent] nvarchar(512) NULL,
                        [CorrelationId] nvarchar(128) NULL,
                        [Action] nvarchar(256) NOT NULL,
                        [ChangeType] int NULL,
                        [EntityType] nvarchar(512) NULL,
                        [EntityId] nvarchar(256) NULL,
                        [OldValues] {jsonColumnType} NULL,
                        [NewValues] {jsonColumnType} NULL,
                        [ChangedFields] {jsonColumnType} NULL,
                        [Success] bit NOT NULL,
                        [ErrorCode] nvarchar(256) NULL,
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
        var createIndexes = string.Join("\n", new[]
        {
            _IndexStatement("ix_audit_log_tenant_time", table, objectName, "[TenantId] ASC, [CreatedAt] ASC"),
            _IndexStatement("ix_audit_log_tenant_action_time", table, objectName, "[TenantId] ASC, [Action] ASC, [CreatedAt] ASC"),
            _IndexStatement("ix_audit_log_tenant_entity_time", table, objectName, "[TenantId] ASC, [EntityType] ASC, [EntityId] ASC, [CreatedAt] ASC"),
            _IndexStatement("ix_audit_log_tenant_actor_time", table, objectName, "[TenantId] ASC, [UserId] ASC, [CreatedAt] ASC"),
            _IndexStatement("ix_audit_log_correlation", table, objectName, "[CorrelationId] ASC"),
        });

        // Release the advisory lock explicitly — Session-scoped locks live for the duration of the
        // SQL session, which under SqlClient connection pooling can outlive `connection.Dispose`
        // and starve subsequent initializers waiting on the same resource.
        var releaseLock = $"EXEC sp_releaseapplock @Resource = N'headless_audit_init:{options.Schema}.{options.TableName}', @LockOwner = N'Session';";

        return $"""
            {acquireLock}

            {createSchema}

            {createTable}

            {createIndexes}

            {releaseLock}
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
