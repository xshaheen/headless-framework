// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

internal sealed class PostgreSqlAuditLogStorageInitializer(
    IOptions<PostgreSqlAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions
) : IHostedLifecycleService, IInitializer
{
    private volatile TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized { get; private set; }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // On a host restart, swap the completion source atomically and cancel the previous promise
        // so waiters from the prior run observe OperationCanceledException rather than hanging.
        // On first start, _completion is the field initializer (no prior waiters to rescue), so
        // skip the cancel — a fresh TCS is never IsCompleted.
        if (_completion.Task.IsCompleted)
        {
            var previous = Interlocked.Exchange(
                ref _completion,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            previous.TrySetCanceled(cancellationToken);
        }

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
        var options = storageOptions.Value;
        var sql = _CreateScript(options);
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                CommandTimeout = (int)providerOptions.Value.CommandTimeout.TotalSeconds,
            };
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        // 42P06: schema_already_exists, 42P07: relation_already_exists (table/index),
        // 42710: duplicate_object, 23505: unique_violation on pg_namespace_nspname_index when
        // two transactions race CREATE SCHEMA IF NOT EXISTS (the IF NOT EXISTS check is not
        // transactional with the catalog insert). The pg_advisory_xact_lock in _CreateScript
        // serializes ours, but a foreign initializer running concurrent DDL can still trigger
        // this path -- absorb it and treat the schema as initialized.
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal static string Qualified(AuditLogStorageOptions options) =>
        $@"""{options.Schema}"".""{options.TableName}""";

    private static string _CreateScript(AuditLogStorageOptions options)
    {
        var table = Qualified(options);
        var createSchema = $"""CREATE SCHEMA IF NOT EXISTS "{options.Schema}";""";
        var jsonColumnType = (options.JsonColumnType ?? AuditLogJsonColumnType.Jsonb).ToSqlFragment();
        var createdAtColumnType = string.IsNullOrWhiteSpace(options.CreatedAtColumnType)
            ? "timestamp with time zone"
            : options.CreatedAtColumnType;

        // Serialize concurrent-startup DDL across replicas with a transaction-scoped advisory
        // lock keyed on (schema, table). Without this, racing CREATE SCHEMA IF NOT EXISTS calls
        // both attempt to insert into pg_namespace and one fails with 23505. The lock is
        // automatically released on COMMIT/ROLLBACK, no explicit release needed.
        var lockResource = $"headless_audit_init:{options.Schema}.{options.TableName}";
        var acquireLock = $"""SELECT pg_advisory_xact_lock(hashtextextended('{lockResource}', 0));""";

        return $"""
            {acquireLock}

            {createSchema}

            CREATE TABLE IF NOT EXISTS {table} (
                "Id" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                "CreatedAt" {createdAtColumnType} NOT NULL,
                "UserId" character varying({AuditLogFieldLimits.UserId}),
                "AccountId" character varying({AuditLogFieldLimits.AccountId}),
                "TenantId" character varying({AuditLogFieldLimits.TenantId}),
                "IpAddress" character varying({AuditLogFieldLimits.IpAddress}),
                "UserAgent" character varying({AuditLogFieldLimits.UserAgent}),
                "CorrelationId" character varying({AuditLogFieldLimits.CorrelationId}),
                "Action" character varying({AuditLogFieldLimits.Action}) NOT NULL,
                "ChangeType" integer,
                "EntityType" character varying({AuditLogFieldLimits.EntityType}),
                "EntityId" character varying({AuditLogFieldLimits.EntityId}),
                "OldValues" {jsonColumnType},
                "NewValues" {jsonColumnType},
                "ChangedFields" {jsonColumnType},
                "Success" boolean NOT NULL,
                "ErrorCode" character varying({AuditLogFieldLimits.ErrorCode}),
                CONSTRAINT "PK_{options.TableName}" PRIMARY KEY ("CreatedAt", "Id")
            );

            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_time" ON {table} ("TenantId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_action_time" ON {table} ("TenantId", "Action", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_entity_time" ON {table} ("TenantId", "EntityType", "EntityId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_tenant_actor_time" ON {table} ("TenantId", "UserId", "CreatedAt");
            CREATE INDEX IF NOT EXISTS "ix_audit_log_correlation" ON {table} ("CorrelationId");
            """;
    }
}
