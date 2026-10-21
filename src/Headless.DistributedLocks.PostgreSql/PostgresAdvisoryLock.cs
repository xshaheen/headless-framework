// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// Implements <see cref="IDbSynchronizationStrategy{TLockCookie}"/> using PostgreSQL advisory locks
/// (https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS).
/// </summary>
/// <remarks>
/// A zero timeout maps to the non-blocking <c>pg_try_advisory_lock</c> family (used by the connection-scoped provider,
/// which drives its own wait loop); a finite/infinite timeout maps to the blocking <c>pg_advisory_lock</c> family with a
/// server-side <c>lock_timeout</c> (used by the transaction-coupled API, where a server-side block is correct). When the
/// connection has a transaction the <c>_xact_</c> variants are emitted, which release on transaction end.
/// <para>
/// When <paramref name="fenceSequence"/> is set, the exclusive non-blocking session acquire draws its fencing token in
/// the same statement that takes the lock (<c>CASE WHEN pg_try_advisory_lock(...) THEN nextval(...) END</c>), on the
/// holding session. A session that dies before the statement returns therefore never holds a token, so it cannot carry
/// one above the next real holder's.
/// </para>
/// </remarks>
/// <param name="isShared"><see langword="true"/> for shared (reader) advisory locks.</param>
/// <param name="timeProvider">Clock for the externally-owned re-entry wait.</param>
/// <param name="allowHashing">Whether long resource names may be hashed into an advisory key.</param>
/// <param name="fenceSequence">
/// The quoted, schema-qualified fence sequence the exclusive session acquire draws from, or <see langword="null"/> for
/// no fencing.
/// </param>
internal sealed class PostgresAdvisoryLock(
    bool isShared,
    TimeProvider timeProvider,
    bool allowHashing = true,
    string? fenceSequence = null
) : IDbSynchronizationStrategy<object>
{
    // A non-null sentinel returned on success; advisory locks carry no per-acquire release state beyond the key.
    private static readonly object _Cookie = new();

    /// <summary>Advisory locks do not natively support upgradeable read locks.</summary>
    public bool IsUpgradeable => false;

    public object GetHeldLockIdentity(string resourceName)
    {
        return PostgreSqlAdvisoryLockKey.FromString(resourceName, allowHashing);
    }

    public async ValueTask<object?> TryAcquireAsync(
        DatabaseConnection connection,
        string resourceName,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        const string savePointName = "headless_distributed_locks_postgres_advisory_lock_acquire";

        // A transaction-scoped success keeps its savepoint, so the SET LOCAL timeouts the acquire sets stay in force until
        // the transaction ends. That is harmless only for a transaction the engine owns and ends with the lease; a
        // caller's transaction would inherit them.
        Debug.Assert(
            !(connection.IsExternallyOwned && connection.HasTransaction),
            "Externally-owned transactions are not supported."
        );

        var key = PostgreSqlAdvisoryLockKey.FromString(resourceName, allowHashing);

        if (
            connection.IsExternallyOwned
            && await _IsHoldingLockAsync(connection, key, cancellationToken).ConfigureAwait(false)
        )
        {
            if (timeout == TimeSpan.Zero)
            {
                return null;
            }

            if (timeout == Timeout.InfiniteTimeSpan)
            {
                throw new InvalidOperationException(
                    "Attempted to acquire a lock that is already held on the same connection (would deadlock)."
                );
            }

            // The lock is already held on this connection; an externally-owned connection cannot acquire it again, so
            // wait out the timeout and report failure. The connection-scoped provider never reaches this path: it owns
            // its connections and never re-acquires the same lock on one.
            await timeProvider.Delay(timeout, cancellationToken).ConfigureAwait(false);

            return null;
        }

        // The acquire command uses SET LOCAL to set statement/lock timeouts; inside a transaction those persist to the
        // end of the transaction, so we wrap in a savepoint we can roll back (except on a transaction-scoped success,
        // where rolling back would release the lock).
        var savePointDefined = await _DefineSavePointIfNeededAsync(connection, savePointName, cancellationToken)
            .ConfigureAwait(false);

        // Only the exclusive non-blocking session acquire is fenced: reader locks never carry a token, and the blocking
        // and transaction-scoped variants back the transaction API, which issues none.
        var fenced =
            fenceSequence is not null
            && !isShared
            && timeout == TimeSpan.Zero
            && !_UseTransactionScopedLock(connection);

        using var acquireCommand = _CreateAcquireCommand(connection, key, timeout, fenced);

        object? acquireCommandResult;

        try
        {
            acquireCommandResult = await acquireCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        // A connection that died took everything its backend held with it, so only a usable one needs cleanup.
        catch (Exception exception) when (connection.CanExecuteQueries)
        {
            var isLockTimeout = _HasSqlState(exception, SqlErrorCodes.PostgreSql.LockTimeout);
            bool isHoldingLock;

            try
            {
                // Rolling back to the savepoint also drops a transaction-scoped lock taken under it.
                await rollBackTransactionTimeoutVariablesIfNeededAsync(isAcquired: false).ConfigureAwait(false);

                // The server may have granted the lock before the client gave up on the statement: a cancel or a
                // client command timeout can land after pg_[try_]advisory_lock returned, and a lock_timeout can race
                // the grant (https://github.com/madelson/DistributedLock/issues/147). Checks use CancellationToken.None
                // because aborting them would leave that question unanswered.
                isHoldingLock = await _IsHoldingLockAsync(connection, key, CancellationToken.None)
                    .ConfigureAwait(false);

                // A lock_timeout that lost the race to the grant is a success. Any other failure must not leave a
                // session lock behind, or it stays held for as long as the pooled connection lives.
                if (isHoldingLock && !isLockTimeout && !_UseTransactionScopedLock(connection))
                {
                    await _ReleaseAsync(connection, key, isTry: true).ConfigureAwait(false);
                }
            }
            catch (Exception cleanupFailure)
            {
                throw new LockCleanupFailedException(
                    [exception, cleanupFailure],
                    $"Acquiring distributed lock '{resourceName}' failed and releasing what the server may have "
                        + "granted failed too; the lock may stay held until its connection or transaction ends."
                );
            }

            if (isLockTimeout)
            {
                return isHoldingLock ? _Cookie : null;
            }

            if (_HasSqlState(exception, SqlErrorCodes.PostgreSql.DeadlockDetected))
            {
                throw new InvalidOperationException(
                    $"The distributed-lock request failed with SqlState '{SqlErrorCodes.PostgreSql.DeadlockDetected}' (deadlock_detected).",
                    exception
                );
            }

            throw;
        }

        var acquired = (fenced, acquireCommandResult) switch
        {
            (true, long) => true,
            (true, DBNull or null) => false, // the fenced CASE yields NULL when the lock was not granted
            (true, _) => (bool?)null,
            (false, DBNull) => true, // pg_advisory_lock (blocking) returns void
            (false, null) => true, // Npgsql 8 returns null instead of DBNull for void
            (false, false) => false,
            (false, true) => true,
            _ => null,
        };

        await rollBackTransactionTimeoutVariablesIfNeededAsync(isAcquired: acquired == true).ConfigureAwait(false);

        return acquired switch
        {
            false => null,
            true => fenced ? new FencedLockCookie(new LockFencingToken((long)acquireCommandResult!)) : _Cookie,
            null => throw new InvalidOperationException(
                $"Unexpected value '{acquireCommandResult}' from the advisory-lock acquire command."
            ),
        };

        async ValueTask rollBackTransactionTimeoutVariablesIfNeededAsync(bool isAcquired)
        {
            if (
                savePointDefined
                // For a transaction-scoped success we can't roll back the savepoint (it would release the lock). Leaking
                // the savepoint is fine: the internally-owned transaction cleans it up on disposal.
                && !(isAcquired && _UseTransactionScopedLock(connection))
            )
            {
                using var rollBackSavePointCommand = connection.CreateCommand();
                rollBackSavePointCommand.SetCommandText("ROLLBACK TO SAVEPOINT " + savePointName);
                await rollBackSavePointCommand.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public ValueTask ReleaseAsync(DatabaseConnection connection, string resourceName, object lockCookie)
    {
        return _ReleaseAsync(
            connection,
            PostgreSqlAdvisoryLockKey.FromString(resourceName, allowHashing),
            isTry: false
        );
    }

    private async ValueTask _ReleaseAsync(DatabaseConnection connection, PostgreSqlAdvisoryLockKey key, bool isTry)
    {
        Debug.Assert(
            !_UseTransactionScopedLock(connection),
            "Transaction-scoped locks are released by the transaction."
        );

        using var command = connection.CreateCommand();
        command.SetCommandText(
            $"SELECT pg_catalog.pg_advisory_unlock{(isShared ? "_shared" : string.Empty)}({key.AddKeyParameters(command)})"
        );

        var result = (bool)(await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false))!;

        if (!isTry && !result)
        {
            throw new InvalidOperationException("Attempted to release a lock that was not held.");
        }
    }

    private static async Task<bool> _IsHoldingLockAsync(
        DatabaseConnection connection,
        PostgreSqlAdvisoryLockKey key,
        CancellationToken cancellationToken
    )
    {
        using var command = connection.CreateCommand();
        command.SetCommandText(
            $"""
            SELECT COUNT(*)
            FROM pg_catalog.pg_locks l
            JOIN pg_catalog.pg_database d ON d.oid = l.database
            WHERE l.locktype = 'advisory'
                AND {key.AddLockFilter(command)}
                AND l.pid = pg_catalog.pg_backend_pid()
                AND d.datname = pg_catalog.current_database()
            """
        );

        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! != 0;
    }

    private DatabaseCommand _CreateAcquireCommand(
        DatabaseConnection connection,
        PostgreSqlAdvisoryLockKey key,
        TimeSpan timeout,
        bool fenced
    )
    {
        var command = connection.CreateCommand();

        var commandText = new StringBuilder();

        // statement_timeout is disabled so everything is driven by lock_timeout.
        commandText.AppendLine("SET LOCAL statement_timeout = 0;");

        // A zero/infinite timeout disables lock_timeout: zero uses pg_try_advisory_lock (which never blocks); infinite
        // blocks until granted.
        var lockTimeoutMs =
            timeout == TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan
                ? 0
                : (long)Math.Ceiling(timeout.TotalMilliseconds);
        commandText.AppendLine(CultureInfo.InvariantCulture, $"SET LOCAL lock_timeout = {lockTimeoutMs};");

        var isTry = timeout == TimeSpan.Zero;

        commandText.Append(fenced ? "SELECT CASE WHEN pg_catalog.pg" : "SELECT pg_catalog.pg");

        if (isTry)
        {
            commandText.Append("_try");
        }

        commandText.Append("_advisory");

        if (_UseTransactionScopedLock(connection))
        {
            commandText.Append("_xact");
        }

        commandText.Append("_lock");

        if (isShared)
        {
            commandText.Append("_shared");
        }

        commandText.Append('(').Append(key.AddKeyParameters(command)).Append(')');

        if (fenced)
        {
            // CASE evaluates THEN only when the lock was granted, so a refused acquire never consumes a token.
            commandText.Append(" THEN pg_catalog.nextval('").Append(fenceSequence).Append("') END");
        }

        commandText.Append(" AS result");

        command.SetCommandText(commandText.ToString());
        command.SetTimeout(timeout);

        return command;
    }

    private static async ValueTask<bool> _DefineSavePointIfNeededAsync(
        DatabaseConnection connection,
        string savePointName,
        CancellationToken cancellationToken
    )
    {
        // For internally-owned connections, HasTransaction is authoritative; without one,
        // SET LOCAL cannot escape the acquire command.
        if (connection is { IsExternallyOwned: false, HasTransaction: false })
        {
            return false;
        }

        using var setSavePointCommand = connection.CreateCommand();
        setSavePointCommand.SetCommandText("SAVEPOINT " + savePointName);

        if (connection.HasTransaction)
        {
            await setSavePointCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }

        // Npgsql does not expose a public read-only flag for an externally-owned connection's active transaction.
        // SAVEPOINT is the exact boundary we need, so classify only PostgreSQL's "no active transaction" state.
        // Cancellation before this returns is clean: no savepoint or SET LOCAL state has been established yet.
        try
        {
            await setSavePointCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception)
            when (string.Equals(exception.SqlState, PostgresErrorCodes.NoActiveSqlTransaction, StringComparison.Ordinal)
            )
        {
            return false;
        }

        return true;
    }

    private static bool _HasSqlState(Exception exception, string sqlState)
    {
        return exception is PostgresException postgresException
            && string.Equals(postgresException.SqlState, sqlState, StringComparison.Ordinal);
    }

    private static bool _UseTransactionScopedLock(DatabaseConnection connection)
    {
        // Only an internally-owned connection carries a transaction (see the assert in TryAcquireAsync); an
        // externally-owned connection's own transaction is invisible here and takes session locks.
        return connection.HasTransaction;
    }
}
