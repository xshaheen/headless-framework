// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Sequences.SqlServer;

/// <summary>Creates the counter schema and table at host startup, once, before any call can reach them.</summary>
#pragma warning disable CA2100 // SQL text is built from the validated schema and table names plus internal column constants.
internal sealed class SqlServerSequencesStorageInitializer(IOptions<SqlServerSequencesOptions> options)
    : HostedInitializer
{
    protected override bool RunOnStartup => options.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        await using var connection = value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(_CreateScript(value), connection);
        command.CommandTimeout = value.CommandTimeoutSeconds;
        // Keyed on the table so replicas starting together serialize their DDL; two configurations that point at
        // different tables do not wait on each other.
        command.Parameters.Add(
            new SqlParameter("LockResource", SqlDbType.NVarChar, 255)
            {
                Value = $"headless_sequences_init:{value.Schema}.{value.TableName}",
            }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _CreateScript(SqlServerSequencesOptions options)
    {
        var table = SqlServerSequencesSchema.Qualified(options);
        var objectName = $"{options.Schema}.{options.TableName}";
        const string collation = SqlServerSequencesSchema.KeyCollation;

        // The applock serializes this initializer across replicas. It is session-scoped, so the outer CATCH releases
        // it before re-throwing: a lock leaked past the throw would stay with the pooled connection and starve the
        // next replica's initializer until the pool physically reset it.
        //
        // IF NOT EXISTS and the CREATE after it are not atomic, so a foreign process running the same DDL can still
        // win the race; each block absorbs "already exists" (2714, 1913, 2759) because the object exists either way.
        //
        // The clustered primary key over exactly (tenant_id, name, partition) is load-bearing, not an index choice:
        // the increment's HOLDLOCK takes its key-range lock on this index, which is what serializes concurrent first
        // calls on a new key. 128 + 128 + 64 nvarchar characters stay under the 900-byte clustered key limit.
        return $"""
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock @Resource = @LockResource, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
            IF @lockResult < 0 THROW 50000, N'Headless.Sequences: failed to acquire the initialization lock on the counter table. Another initializer may be holding it.', 1;

            BEGIN TRY
                BEGIN TRAN;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{options.Schema}')
                        EXEC(N'CREATE SCHEMA [{options.Schema}]');
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF OBJECT_ID(N'{objectName}', N'U') IS NULL
                    BEGIN
                        CREATE TABLE {table} (
                            {SqlServerSequencesSchema.TenantId} nvarchar({SequenceFieldLimits.TenantIdMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerSequencesSchema.Name} nvarchar({SequenceFieldLimits.NameMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerSequencesSchema.Partition} nvarchar({SequenceFieldLimits.PartitionMaxLength}) COLLATE {collation} NOT NULL,
                            {SqlServerSequencesSchema.Value} bigint NOT NULL,
                            {SqlServerSequencesSchema.CreatedAt} datetime2 NOT NULL,
                            {SqlServerSequencesSchema.UpdatedAt} datetime2 NOT NULL,
                            CONSTRAINT [PK_{options.TableName}] PRIMARY KEY CLUSTERED (
                                {SqlServerSequencesSchema.TenantId} ASC,
                                {SqlServerSequencesSchema.Name} ASC,
                                {SqlServerSequencesSchema.Partition} ASC
                            )
                        );
                    END;
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
