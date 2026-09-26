// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Fencing.SqlServer;

/// <summary>
/// Creates the fencing schema, lease table, indexes, and generation sequence at host startup, once, before any call
/// can reach them.
/// </summary>
#pragma warning disable CA2100 // SQL text is built from the validated schema name plus internal object and column constants.
internal sealed class SqlServerFencingStorageInitializer(
    IOptions<SqlServerFencingOptions> options,
    IOptions<FencingStorageOptions> storageOptions
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
                Value = $"headless_fencing_init:{schema}.{SqlServerFencingSchema.TableName}",
            }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _CreateScript(string schema)
    {
        var table = SqlServerFencingSchema.QualifiedTable(schema);
        var sequence = SqlServerFencingSchema.QualifiedSequence(schema);
        var tableName = $"{schema}.{SqlServerFencingSchema.TableName}";
        var sequenceName = $"{schema}.{SqlServerFencingSchema.SequenceName}";
        const string collation = SqlServerFencingSchema.KeyCollation;
        const string t = SqlServerFencingSchema.TableName;

        // The applock serializes this initializer across replicas. It is session-scoped, so the outer CATCH releases
        // it before re-throwing: a lock leaked past the throw would stay with the pooled connection and starve the
        // next replica's initializer until the pool physically reset it.
        //
        // IF NOT EXISTS and the CREATE after it are not atomic, so a foreign process running the same DDL can still
        // win the race; each block absorbs "already exists" (2714, 1913, 2759) because the object exists either way.
        //
        // The clustered primary key over exactly (tenant_id, kind, resource) is load-bearing, not an index choice:
        // a grant's HOLDLOCK read takes its key-range lock on this index, which is what serializes concurrent first
        // grants of a new key. The key-part limits total 448 nvarchar characters, under the 900-byte clustered key
        // limit. One store-wide sequence issues every generation, so a lease granted again after its row was purged
        // still gets a generation above every earlier one. The active index serves the sweep's keyset walk in
        // (expires_at, tenant_id, resource) order; the ended index serves purge.
        return $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Fencing: failed to acquire the initialization lock on the lease table. Another initializer may be holding it.', 1;

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
                            {SqlServerFencingSchema.TenantId} nvarchar({FencingFieldLimits.TenantIdMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerFencingSchema.Kind} nvarchar({FencingFieldLimits.KindMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerFencingSchema.Resource} nvarchar({FencingFieldLimits.ResourceMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerFencingSchema.Generation} bigint NOT NULL,
                            {SqlServerFencingSchema.State} smallint NOT NULL,
                            {SqlServerFencingSchema.GrantedAt} datetimeoffset(7) NOT NULL,
                            {SqlServerFencingSchema.ExpiresAt} datetimeoffset(7) NOT NULL,
                            {SqlServerFencingSchema.EndedAt} datetimeoffset(7) NULL,
                            CONSTRAINT [PK_{t}] PRIMARY KEY CLUSTERED (
                                {SqlServerFencingSchema.TenantId} ASC,
                                {SqlServerFencingSchema.Kind} ASC,
                                {SqlServerFencingSchema.Resource} ASC
                            ),
                            CONSTRAINT [CK_{t}_generation] CHECK ({SqlServerFencingSchema.Generation} > 0),
                            CONSTRAINT [CK_{t}_state] CHECK (
                                {SqlServerFencingSchema.State} BETWEEN {SqlServerFencingSchema.Active} AND {SqlServerFencingSchema.Abandoned}
                            ),
                            CONSTRAINT [CK_{t}_ended_at] CHECK (
                                ({SqlServerFencingSchema.State} = {SqlServerFencingSchema.Active} AND {SqlServerFencingSchema.EndedAt} IS NULL)
                                OR ({SqlServerFencingSchema.State} <> {SqlServerFencingSchema.Active} AND {SqlServerFencingSchema.EndedAt} IS NOT NULL)
                            )
                        );
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{tableName}') AND name = N'IX_{t}_active_expiry')
                        CREATE INDEX [IX_{t}_active_expiry]
                            ON {table} (
                                {SqlServerFencingSchema.Kind},
                                {SqlServerFencingSchema.ExpiresAt},
                                {SqlServerFencingSchema.TenantId},
                                {SqlServerFencingSchema.Resource}
                            )
                            WHERE {SqlServerFencingSchema.State} = {SqlServerFencingSchema.Active};
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{tableName}') AND name = N'IX_{t}_ended')
                        CREATE INDEX [IX_{t}_ended]
                            ON {table} ({SqlServerFencingSchema.Kind}, {SqlServerFencingSchema.EndedAt})
                            WHERE {SqlServerFencingSchema.State} <> {SqlServerFencingSchema.Active};
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
