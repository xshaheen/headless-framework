// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // DDL identifiers are validated, sanitized, and quoted before interpolation.
internal sealed class SqlServerDistributedLocksStorageInitializer(IOptions<SqlServerDistributedLockOptions> options)
    : HostedInitializer
{
    protected override bool RunOnStartup => options.Value.EnableFencing;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = options.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSequenceAsync(connection, options.Value, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask EnsureSequenceAsync(
        SqlConnection connection,
        SqlServerDistributedLockOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var schema = options.Schema;
        var sequenceName = SqlServerIdentifier.FenceSequenceName(options.KeyPrefix);
        var lockResource = SqlServerResourceName.Encode($"{options.KeyPrefix}init:{schema}.{sequenceName}");
        var qualifiedSequence = $"{SqlServerIdentifier.Quote(schema)}.{SqlServerIdentifier.Quote(sequenceName)}";
        var lockTimeoutMs = options.CommandTimeout.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(options.CommandTimeout.TotalMilliseconds);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(options.CommandTimeout);
        command.CommandText = $$"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = @lockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = @lockTimeout,
                @DbPrincipal = N'public';

            IF @lockResult < 0
                THROW 50000, N'Headless.DistributedLocks.SqlServer: failed to acquire fencing sequence initialization lock.', 1;

            BEGIN TRY
                IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @schema)
                BEGIN
                    DECLARE @createSchema nvarchar(max) = N'CREATE SCHEMA {{SqlServerIdentifier.Quote(schema)}}';
                    EXEC sys.sp_executesql @createSchema;
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.sequences s
                    JOIN sys.schemas sc ON sc.schema_id = s.schema_id
                    WHERE s.name = @sequenceName AND sc.name = @schema
                )
                BEGIN
                    DECLARE @createSequence nvarchar(max) =
                        N'CREATE SEQUENCE {{qualifiedSequence}} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE';
                    EXEC sys.sp_executesql @createSequence;
                END;

                EXEC sys.sp_releaseapplock @Resource = @lockResource, @LockOwner = N'Session', @DbPrincipal = N'public';
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    IF APPLOCK_MODE(N'public', @lockResource, N'Session') <> N'NoLock'
                        EXEC sys.sp_releaseapplock @Resource = @lockResource, @LockOwner = N'Session', @DbPrincipal = N'public';
                END TRY
                BEGIN CATCH
                END CATCH;

                THROW;
            END CATCH;
            """;
        command.Parameters.AddWithValue("lockResource", lockResource);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("sequenceName", sequenceName);
        command.Parameters.AddWithValue("lockTimeout", lockTimeoutMs);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
#pragma warning restore CA2100
