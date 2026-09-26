// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Idempotency.SqlServer;

/// <summary>
/// Creates the idempotency schema, generation sequence, record table, and retention index at host startup, once, before any call can reach
/// them.
/// </summary>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class SqlServerIdempotencyStorageInitializer(
    IOptions<SqlServerIdempotencyOptions> options,
    IOptions<IdempotencyStorageOptions> storageOptions
) : HostedInitializer
{
    protected override bool RunOnStartup => options.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        var schema = storageOptions.Value.Schema;
        await using var connection = value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(schema), connection);
        command.CommandTimeout = value.CommandTimeoutSeconds;
        // Keyed on the table so replicas starting together serialize their DDL; two configurations that point at
        // different schemas do not wait on each other.
        command.Parameters.Add(
            new SqlParameter("LockResource", SqlDbType.NVarChar, 255)
            {
                Value = $"headless_idempotency_init:{schema}.{SqlServerIdempotencySchema.TableName}",
            }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _CreateScript(string schema)
    {
        var table = SqlServerIdempotencySchema.QualifiedTable(schema);
        var tableName = $"{schema}.{SqlServerIdempotencySchema.TableName}";
        var sequence = SqlServerIdempotencySchema.QualifiedSequence(schema);
        var sequenceName = $"{schema}.{SqlServerIdempotencySchema.SequenceName}";
        const string collation = SqlServerIdempotencySchema.KeyCollation;
        const string t = SqlServerIdempotencySchema.TableName;

        // The applock serializes this initializer across replicas. It is session-scoped, so the outer CATCH releases
        // it before re-throwing: a lock leaked past the throw would stay with the pooled connection and starve the
        // next replica's initializer until the pool physically reset it.
        //
        // IF NOT EXISTS and the CREATE after it are not atomic, so a foreign process running the same DDL can still
        // win the race; each block absorbs "already exists" (2714, 1913, 2759) because the object exists either way.
        //
        // The clustered primary key over exactly (tenant_id, idempotency_key) is load-bearing, not an index choice: a
        // lock-or-insert's HOLDLOCK read takes its key-range lock on this index, which is what serializes concurrent
        // first admissions of a new key without a duplicate-key error. The key parts total 384 nvarchar characters,
        // under the 900-byte clustered key limit. A completed record always carries its result and contract and a
        // pending one never does. A pending record names an admitted attempt's generation exactly when it carries that
        // attempt's lease expiry; a completed one keeps the generation that completed it and no lease. One store-wide
        // sequence issues every generation, so a key admitted again after its record was purged still gets a
        // generation above every earlier attempt's. The retention index serves the purge.
        return $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Idempotency: failed to acquire the initialization lock on the record table. Another initializer may be holding it.', 1;

            BEGIN TRY
                BEGIN TRAN;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{schema}')
                        EXEC(N'CREATE SCHEMA [{schema}]');
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF OBJECT_ID(N'{sequenceName}', N'SO') IS NULL
                        CREATE SEQUENCE {sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                    BEGIN
                        CREATE TABLE {table} (
                            {SqlServerIdempotencySchema.TenantId} nvarchar({IdempotencyFieldLimits.TenantIdMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerIdempotencySchema.Key} nvarchar({IdempotencyFieldLimits.KeyMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerIdempotencySchema.Status} smallint NOT NULL,
                            {SqlServerIdempotencySchema.FingerprintAlgorithm} nvarchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerIdempotencySchema.Fingerprint} varbinary({IdempotencyFieldLimits.FingerprintMaxLength}) NOT NULL,
                            {SqlServerIdempotencySchema.Generation} bigint NULL,
                            {SqlServerIdempotencySchema.LeaseExpiresAt} datetimeoffset(7) NULL,
                            {SqlServerIdempotencySchema.Result} varbinary(max) NULL,
                            {SqlServerIdempotencySchema.ResultContract} nvarchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE {collation} NULL,
                            {SqlServerIdempotencySchema.RetentionUntil} datetimeoffset(7) NOT NULL,
                            CONSTRAINT [PK_{t}] PRIMARY KEY CLUSTERED (
                                {SqlServerIdempotencySchema.TenantId} ASC,
                                {SqlServerIdempotencySchema.Key} ASC
                            ),
                            CONSTRAINT [CK_{t}_status] CHECK (
                                {SqlServerIdempotencySchema.Status} BETWEEN {SqlServerIdempotencySchema.Pending} AND {SqlServerIdempotencySchema.Completed}
                            ),
                            CONSTRAINT [CK_{t}_fingerprint] CHECK (DATALENGTH({SqlServerIdempotencySchema.Fingerprint}) > 0),
                            CONSTRAINT [CK_{t}_result] CHECK (
                                ({SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Completed} AND {SqlServerIdempotencySchema.Result} IS NOT NULL AND {SqlServerIdempotencySchema.ResultContract} IS NOT NULL)
                                OR ({SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending} AND {SqlServerIdempotencySchema.Result} IS NULL AND {SqlServerIdempotencySchema.ResultContract} IS NULL)
                            ),
                            CONSTRAINT [CK_{t}_generation] CHECK ({SqlServerIdempotencySchema.Generation} IS NULL OR {SqlServerIdempotencySchema.Generation} > 0),
                            CONSTRAINT [CK_{t}_lease] CHECK (
                                ({SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Completed} AND {SqlServerIdempotencySchema.Generation} IS NOT NULL AND {SqlServerIdempotencySchema.LeaseExpiresAt} IS NULL)
                                OR ({SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending} AND {SqlServerIdempotencySchema.Generation} IS NULL AND {SqlServerIdempotencySchema.LeaseExpiresAt} IS NULL)
                                OR ({SqlServerIdempotencySchema.Status} = {SqlServerIdempotencySchema.Pending} AND {SqlServerIdempotencySchema.Generation} IS NOT NULL AND {SqlServerIdempotencySchema.LeaseExpiresAt} IS NOT NULL)
                            )
                        );
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{tableName}') AND name = N'IX_{t}_retention_until')
                        CREATE INDEX [IX_{t}_retention_until] ON {table} ({SqlServerIdempotencySchema.RetentionUntil});
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                COMMIT TRAN;

                EXEC sp_releaseapplock @Resource = @LockResource, @LockOwner = N'Session';
            END TRY
            BEGIN CATCH
                -- XACT_STATE() is 1 for an active and -1 for a doomed transaction; both need the rollback, and 0 has
                -- none to roll back, where a ROLLBACK would raise a second error that hides the first.
                IF XACT_STATE() <> 0 ROLLBACK TRAN;

                -- A failing release must not replace the original error, so it is swallowed here; closing the
                -- connection releases a session lock as a last resort.
                BEGIN TRY
                    IF APPLOCK_MODE('public', @LockResource, 'Session') <> 'NoLock'
                        EXEC sp_releaseapplock @Resource = @LockResource, @LockOwner = N'Session';
                END TRY
                BEGIN CATCH
                END CATCH;

                THROW;
            END CATCH;
            """;
    }
}
#pragma warning restore CA2100
