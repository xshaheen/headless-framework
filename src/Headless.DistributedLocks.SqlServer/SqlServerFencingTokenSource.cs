// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // Sequence identifiers are validated, sanitized, and quoted before interpolation.
/// <summary>
/// <see cref="IFencingTokenSource"/> implementation that reads the next value from the SQL Server
/// <c>bigint</c> sequence created by <see cref="SqlServerDistributedLocksStorageInitializer"/>. Tokens
/// are strictly increasing across all processes connected to the same database.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="SqlServerDistributedLockOptions.EnableFencing"/> is <see langword="false"/>,
/// <see cref="NextAsync"/> returns <see langword="null"/> immediately without touching the database.
/// </para>
/// <para>
/// The sequence is created lazily on the first <see cref="NextAsync"/> call when the
/// <see cref="SqlServerDistributedLocksStorageInitializer"/> has not already run (for example when the
/// provider is used without an <c>IHost</c>). Lazy creation is double-checked with
/// <c>_ensureGate</c> so multiple concurrent first-callers do not issue duplicate DDL.
/// </para>
/// <para>
/// When a <see cref="Microsoft.Data.SqlClient.SqlConnection"/> from the just-acquired lock handle is
/// supplied, <see cref="NextAsync"/> reuses it to avoid opening a second connection per exclusive acquire.
/// </para>
/// </remarks>
internal sealed class SqlServerFencingTokenSource(IOptions<SqlServerDistributedLockOptions> options)
    : IFencingTokenSource,
        IDisposable
{
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private bool _sequenceEnsured;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="null"/> immediately when fencing is disabled via
    /// <see cref="SqlServerDistributedLockOptions.EnableFencing"/>. Otherwise ensures the sequence exists
    /// (at most once per source instance), then issues <c>SELECT NEXT VALUE FOR &lt;schema&gt;.&lt;sequence&gt;</c>
    /// on the supplied connection or a new short-lived connection.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async ValueTask<long?> NextAsync(
        string resource,
        DbConnection? connection = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!options.Value.EnableFencing)
        {
            return null;
        }

        // The HostedInitializer pre-warms the sequence on hosted startup; the lazy ensure-path is the self-heal for
        // consumers that resolve the provider without starting the host (the same pattern the Postgres source uses).
        // It runs on its own short-lived connection so its applock-guarded DDL never shares the held lock's session.
        // Gated by _sequenceEnsured so the once-per-source IF-NOT-EXISTS check runs at most once.
        if (!Volatile.Read(ref _sequenceEnsured))
        {
            await using var ensureConnection = options.Value.CreateConnection();
            await ensureConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await _EnsureSequenceAsync(ensureConnection, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<long?> _NextAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = SqlServerApplicationLock.GetCommandTimeoutSeconds(options.Value.CommandTimeout);
        command.CommandText =
            $"SELECT NEXT VALUE FOR {SqlServerIdentifier.Quote(options.Value.Schema)}.{SqlServerIdentifier.Quote(SqlServerIdentifier.FenceSequenceName(options.Value.KeyPrefix))}";

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    private async ValueTask _EnsureSequenceAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _sequenceEnsured))
        {
            return;
        }

        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_sequenceEnsured)
            {
                return;
            }

            await SqlServerDistributedLocksStorageInitializer
                .EnsureSequenceAsync(connection, options.Value, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _sequenceEnsured, value: true);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    /// <summary>Disposes the internal gate semaphore.</summary>
    public void Dispose()
    {
        _ensureGate.Dispose();
    }
}
#pragma warning restore CA2100
