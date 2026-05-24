// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

public sealed class SqlServerAuditLogStorageInitializer(
    IOptions<SqlServerAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions
) : IAuditLogStorageInitializer, IHostedService, IInitializer
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(storageOptions.Value), connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string Qualified(AuditLogStorageOptions options) =>
        string.IsNullOrWhiteSpace(options.Schema)
            ? $"[{options.TableName}]"
            : $"[{options.Schema}].[{options.TableName}]";

    internal static string ObjectName(AuditLogStorageOptions options) =>
        string.IsNullOrWhiteSpace(options.Schema) ? options.TableName : $"{options.Schema}.{options.TableName}";

    private static string _CreateScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var objectName = ObjectName(options);
        var jsonColumnType = options.JsonColumnType ?? "nvarchar(max)";
        var createSchema = string.IsNullOrWhiteSpace(options.Schema)
            ? string.Empty
            : $"""
                BEGIN TRY
                    IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{options.Schema}')
                        EXEC(N'CREATE SCHEMA [{options.Schema}]');
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;
              """;

        return $"""
            {createSchema}

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
                    CREATE NONCLUSTERED INDEX [ix_audit_log_tenant_time] ON {table} ([TenantId] ASC, [CreatedAt] ASC);
                    CREATE NONCLUSTERED INDEX [ix_audit_log_tenant_action_time] ON {table} ([TenantId] ASC, [Action] ASC, [CreatedAt] ASC);
                    CREATE NONCLUSTERED INDEX [ix_audit_log_tenant_entity_time] ON {table} ([TenantId] ASC, [EntityType] ASC, [EntityId] ASC, [CreatedAt] ASC);
                    CREATE NONCLUSTERED INDEX [ix_audit_log_tenant_actor_time] ON {table} ([TenantId] ASC, [UserId] ASC, [CreatedAt] ASC);
                    CREATE NONCLUSTERED INDEX [ix_audit_log_correlation] ON {table} ([CorrelationId] ASC);
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
            END CATCH;
            """;
    }
}
