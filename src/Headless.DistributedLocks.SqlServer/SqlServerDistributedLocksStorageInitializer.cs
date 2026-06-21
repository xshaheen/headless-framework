// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // DDL identifiers are validated, sanitized, and quoted before interpolation.
/// <summary>
/// Hosted startup initializer that creates the SQL Server fencing sequence used by
/// <see cref="SqlServerFencingTokenSource"/> to issue strictly-increasing fencing tokens.
/// </summary>
/// <remarks>
/// <para>
/// Runs only when <see cref="SqlServerDistributedLockOptions.EnableFencing"/> is
/// <see langword="true"/>. Creates (if absent) the configured schema and a <c>bigint</c> sequence named
/// <c>{KeyPrefix}_headless_distlocks_fence</c> (truncated to 128 characters if necessary) inside that
/// schema. Schema and sequence creation are guarded by a session-scoped <c>sp_getapplock</c> so
/// concurrent initializers on multiple nodes do not race on DDL.
/// </para>
/// <para>
/// The initializer is idempotent: re-running it when the sequence already exists is a no-op.
/// <see cref="SqlServerFencingTokenSource"/> also calls <see cref="EnsureSequenceAsync"/> lazily for
/// callers that resolve the provider without hosting (no <c>IHostedService</c> startup).
/// </para>
/// </remarks>
internal sealed class SqlServerDistributedLocksStorageInitializer(IOptions<SqlServerDistributedLockOptions> options)
    : HostedInitializer
{
    /// <inheritdoc/>
    protected override bool RunOnStartup => options.Value.EnableFencing;

    /// <summary>
    /// Opens a new connection and calls <see cref="EnsureSequenceAsync"/> to create the schema and fencing
    /// sequence when they do not yet exist.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the initialization.</param>
    /// <remarks>Underlying <see cref="Microsoft.Data.SqlClient.SqlException"/> errors propagate to the caller.</remarks>
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = options.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSequenceAsync(connection, options.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the fencing sequence (and its owning schema) exist in the database, using a session-scoped
    /// <c>sp_getapplock</c> to serialize concurrent DDL across nodes. Idempotent: a no-op when the schema
    /// and sequence are already present.
    /// </summary>
    /// <param name="connection">An open SQL Server connection on which to run the DDL.</param>
    /// <param name="options">
    /// Provider options supplying <see cref="SqlServerDistributedLockOptions.Schema"/>,
    /// <see cref="SqlServerDistributedLockOptions.KeyPrefix"/>, and
    /// <see cref="SqlServerDistributedLockOptions.CommandTimeout"/>.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <remarks>Underlying <see cref="Microsoft.Data.SqlClient.SqlException"/> errors propagate to the caller.</remarks>
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
        var lockTimeoutMs =
            options.CommandTimeout.TotalMilliseconds >= int.MaxValue
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
