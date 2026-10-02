// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // Sequence identifiers are validated, sanitized, and quoted before interpolation.
/// <summary>
/// <see cref="IFencingTokenSource"/> implementation that reads the next value from the SQL Server
/// <c>bigint</c> sequence created by <see cref="SqlServerDistributedLocksSchemaContribution"/>. Tokens
/// are strictly increasing across all processes connected to the same database.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="SqlServerDistributedLockOptions.EnableFencing"/> is <see langword="false"/>,
/// <see cref="NextAsync"/> returns <see langword="null"/> immediately without touching the database.
/// </para>
/// <para>
/// The Headless schema runner creates the sequence at host startup; this source never creates it lazily. DDL issued
/// from an acquire would bypass the runner's history, verify mode, and exported script.
/// </para>
/// <para>
/// When a <see cref="Microsoft.Data.SqlClient.SqlConnection"/> from the just-acquired lock handle is
/// supplied, <see cref="NextAsync"/> reuses it to avoid opening a second connection per exclusive acquire.
/// </para>
/// </remarks>
internal sealed class SqlServerFencingTokenSource(
    IOptions<SqlServerDistributedLockOptions> options,
    IOptions<DistributedLocksStorageOptions> storageOptions
) : IFencingTokenSource
{
    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="null"/> immediately when fencing is disabled via
    /// <see cref="SqlServerDistributedLockOptions.EnableFencing"/>. Otherwise issues
    /// <c>SELECT NEXT VALUE FOR &lt;schema&gt;.&lt;sequence&gt;</c> on the supplied connection or a new short-lived
    /// connection.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async ValueTask<LockFencingToken?> NextAsync(
        string resource,
        DbConnection? connection = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!options.Value.EnableFencing)
        {
            return null;
        }

        // Reuse the lock handle's own open SqlConnection when the provider lends it, avoiding a second connection per
        // exclusive acquire. Otherwise (incompatible handle or none) open our own.
        if (connection is SqlConnection sqlConnection && sqlConnection.State == System.Data.ConnectionState.Open)
        {
            return await _NextAsync(sqlConnection, cancellationToken).ConfigureAwait(false);
        }

        await using var ownedConnection = options.Value.CreateConnection();
        await ownedConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await _NextAsync(ownedConnection, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<LockFencingToken> _NextAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(options.Value.CommandTimeout);
        command.CommandText =
            $"SELECT NEXT VALUE FOR {SqlServerIdentifier.Quote(storageOptions.Value.Schema)}.{SqlServerIdentifier.Quote(SqlServerIdentifier.FenceSequenceName(options.Value.KeyPrefix))}";

        var value = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );

        return new LockFencingToken(value);
    }
}
#pragma warning restore CA2100
