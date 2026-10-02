// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

#pragma warning disable CA2100 // The only interpolated value is the schema, validated against the PostgreSQL identifier rules at startup and quoted here.
/// <summary>
/// Implements <see cref="IFencingTokenSource"/> over a PostgreSQL database sequence named
/// <c>headless_distributed_locks_fence</c>, created inside the feature-owned
/// <see cref="DistributedLocksStorageOptions.Schema"/>. Each call to <see cref="NextAsync"/> returns the next
/// value from the sequence, guaranteeing a strictly-increasing token across all processes connected to the same
/// database.
/// </summary>
/// <remarks>
/// <para>
/// The Headless schema runner creates the sequence at host startup from
/// <see cref="PostgreSqlDistributedLocksSchemaContribution"/>; this source never creates it lazily. DDL issued from
/// an acquire would bypass the runner's history, verify mode, and exported script.
/// </para>
/// <para>
/// This source always opens a fresh pooled connection from its owned <see cref="NpgsqlDataSource"/>
/// regardless of the handle connection supplied to <see cref="NextAsync"/>; the handle connection is
/// intentionally ignored because the multiplexing engine may share it with other lock operations.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplying the command timeout.</param>
/// <param name="storageOptions">Supplies the schema that holds the sequence.</param>
/// <param name="dataSource">
/// The shared <see cref="NpgsqlDataSource"/> injected by the DI registration. Not disposed here;
/// disposal is owned by <see cref="PostgresLockDataSource"/>.
/// </param>
internal sealed class PostgresFencingTokenSource(
    IOptions<PostgreSqlDistributedLockOptions> options,
    IOptions<DistributedLocksStorageOptions> storageOptions,
    NpgsqlDataSource dataSource
) : IFencingTokenSource
{
    private readonly TimeSpan _commandTimeout = options.Value.CommandTimeout;
    private readonly string _nextValueSql =
        $"SELECT nextval('{PostgreSqlDistributedLocksSchemaContribution.QualifiedSequence(storageOptions.Value.Schema)}')";

    /// <inheritdoc/>
    /// <remarks>
    /// Always opens a fresh connection from the owned <see cref="NpgsqlDataSource"/>; the optional
    /// <paramref name="connection"/> argument is intentionally ignored. Underlying Npgsql errors propagate
    /// to the caller.
    /// </remarks>
    /// <param name="resource">Ignored; the Postgres sequence is shared across all resources.</param>
    /// <param name="connection">
    /// Ignored; the token is always issued on a fresh pooled connection so the handle connection is not
    /// disturbed.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the sequence command.</param>
    /// <returns>A token carrying the next strictly-increasing sequence value.</returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled before the sequence value is returned.
    /// </exception>
    public async ValueTask<LockFencingToken?> NextAsync(
        string resource,
        DbConnection? connection = null,
        CancellationToken cancellationToken = default
    )
    {
        // The handle connection comes from the multiplexing engine's pool and may be shared/dedicated under its own
        // monitoring; Postgres always issues the token on a freshly-opened pooled connection from its owned data
        // source, so the optional handle connection is intentionally ignored here.
        _ = connection;

        await using var pooledConnection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = pooledConnection.CreateCommand();
        command.CommandText = _nextValueSql;
        command.CommandTimeout = (int)_commandTimeout.TotalSeconds;

        var value = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

        return new LockFencingToken(value);
    }
}
#pragma warning restore CA2100
