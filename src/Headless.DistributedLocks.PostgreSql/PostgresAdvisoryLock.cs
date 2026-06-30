// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
/// </remarks>
internal sealed partial class PostgresAdvisoryLock(bool isShared, TimeProvider timeProvider, bool allowHashing = true)
    : IDbSynchronizationStrategy<object>
{
    // A non-null sentinel returned on success; advisory locks carry no per-acquire release state beyond the key.
    private static readonly object _Cookie = new();

    /// <summary>Advisory locks do not natively support upgradeable read locks.</summary>
    public bool IsUpgradeable => false;

    public object GetHeldLockIdentity(string resourceName)
    {
        return PostgresAdvisoryLockKey.FromString(resourceName, allowHashing);
    }

    public async ValueTask<object?> TryAcquireAsync(
        DatabaseConnection connection,
        string resourceName,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        const string savePointName = "headless_distributed_locks_postgres_advisory_lock_acquire";

        var key = PostgresAdvisoryLockKey.FromString(resourceName, allowHashing);

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
            // wait out the timeout and report failure. This degenerate path is reached only by the externally-owned
            // transaction API, never by the connection-scoped provider (which owns its connections and never
            // re-acquires the same lock on one).
            await timeProvider.Delay(timeout, cancellationToken).ConfigureAwait(false);

            return null;
        }

        // Capture timeout settings before setting a savepoint when acquiring an externally-owned transaction-scoped lock:
        // on success we can't roll back the savepoint (it would release the lock), so we restore the settings explicitly.
        var capturedTimeoutSettings = await _CaptureTimeoutSettingsIfNeededAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        // The acquire command uses SET LOCAL to set statement/lock timeouts; inside a transaction those persist to the
        // end of the transaction, so we wrap in a savepoint we can roll back (except on a transaction-scoped success,
        // where rolling back would release the lock).
        var savePointDefined = await _DefineSavePointIfNeededAsync(connection, savePointName, cancellationToken)
            .ConfigureAwait(false);

        using var acquireCommand = _CreateAcquireCommand(connection, key, timeout);

        object? acquireCommandResult;

        try
        {
            acquireCommandResult = await acquireCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await rollBackTransactionTimeoutVariablesIfNeededAsync(acquired: false).ConfigureAwait(false);
            await _RestoreTimeoutSettingsIfNeededAsync(capturedTimeoutSettings, connection).ConfigureAwait(false);

            if (exception is PostgresException postgresException)
            {
                switch (postgresException.SqlState)
                {
                    // lock_timeout (https://www.postgresql.org/docs/current/errcodes-appendix.html). A PostgreSQL race
                    // means we might have actually acquired the lock just before timing out, so re-check. We use
                    // CancellationToken.None because if we DO hold the lock it would be invalid to abort the check.
                    // See https://github.com/madelson/DistributedLock/issues/147.
                    case "55P03":
                        return await _IsHoldingLockAsync(connection, key, CancellationToken.None).ConfigureAwait(false)
                            ? _Cookie
                            : null;
                    // deadlock_detected
                    case "40P01":
                        throw new InvalidOperationException(
                            $"The distributed-lock request failed with SqlState '{postgresException.SqlState}' (deadlock_detected).",
                            exception
                        );
                    default:
                        break;
                }
            }

            if (
                exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested
                // Transaction-scoped locks can only be released by rolling back; the savepoint rollback above already
                // handled that, and the caller will dispose the transaction.
                && !_UseTransactionScopedLock(connection)
            )
            {
                // We bailed mid-acquire; make sure we didn't leave a lock behind.
                await _ReleaseAsync(connection, key, isTry: true).ConfigureAwait(false);
            }

            throw;
        }

        var acquired = acquireCommandResult switch
        {
            DBNull => true, // pg_advisory_lock (blocking) returns void
            null => true, // Npgsql 8 returns null instead of DBNull for void
            false => false,
            true => true,
            _ => (bool?)null,
        };

        await rollBackTransactionTimeoutVariablesIfNeededAsync(acquired: acquired == true).ConfigureAwait(false);
        await _RestoreTimeoutSettingsIfNeededAsync(capturedTimeoutSettings, connection).ConfigureAwait(false);

        return acquired switch
        {
            false => null,
            true => _Cookie,
            null => throw new InvalidOperationException(
                $"Unexpected value '{acquireCommandResult}' from the advisory-lock acquire command."
            ),
        };

        async ValueTask rollBackTransactionTimeoutVariablesIfNeededAsync(bool acquired)
        {
            if (
                savePointDefined
                // For a transaction-scoped success we can't roll back the savepoint (it would release the lock). Leaking
                // the savepoint is fine: an internally-owned transaction cleans it up on disposal, and an externally-owned
                // transaction must keep it or lose the lock.
                && !(acquired && _UseTransactionScopedLock(connection))
            )
            {
                using var rollBackSavePointCommand = connection.CreateCommand();
                rollBackSavePointCommand.SetCommandText("ROLLBACK TO SAVEPOINT " + savePointName);
                await rollBackSavePointCommand.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public ValueTask ReleaseAsync(DatabaseConnection connection, string resourceName, object lockCookie) =>
        _ReleaseAsync(connection, PostgresAdvisoryLockKey.FromString(resourceName, allowHashing), isTry: false);

    private async ValueTask _ReleaseAsync(DatabaseConnection connection, PostgresAdvisoryLockKey key, bool isTry)
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
        PostgresAdvisoryLockKey key,
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
        PostgresAdvisoryLockKey key,
        TimeSpan timeout
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

        commandText.Append("SELECT pg_catalog.pg");

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

        commandText.Append('(').Append(key.AddKeyParameters(command)).Append(") AS result");

        command.SetCommandText(commandText.ToString());
        command.SetTimeout(timeout);

        return command;
    }

    private static async ValueTask<CapturedTimeoutSettings?> _CaptureTimeoutSettingsIfNeededAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken
    )
    {
        // Only relevant for an externally-owned transaction-scoped acquire (where we can't roll back the savepoint on
        // success and must restore the settings explicitly).
        if (!(connection.IsExternallyOwned && _UseTransactionScopedLock(connection)))
        {
            return null;
        }

        var statementTimeout = await getCurrentSettingAsync("statement_timeout").ConfigureAwait(false);
        var lockTimeout = await getCurrentSettingAsync("lock_timeout").ConfigureAwait(false);

        return new CapturedTimeoutSettings(statementTimeout!, lockTimeout!);

        async ValueTask<string?> getCurrentSettingAsync(string settingName)
        {
            using var command = connection.CreateCommand();
            command.SetCommandText($"SELECT current_setting('{settingName}', 'true') AS {settingName};");

            return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<bool> _DefineSavePointIfNeededAsync(
        DatabaseConnection connection,
        string savePointName,
        CancellationToken cancellationToken
    )
    {
        // For internally-owned connections, HasTransaction is authoritative; without one,
        // SET LOCAL cannot escape the acquire command.
        if (!connection.IsExternallyOwned && !connection.HasTransaction)
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

    private static async ValueTask _RestoreTimeoutSettingsIfNeededAsync(
        CapturedTimeoutSettings? settings,
        DatabaseConnection connection
    )
    {
        if (settings is not { } captured)
        {
            return;
        }

        using var command = connection.CreateCommand();

        var commandText = new StringBuilder();
        commandText.AppendLine(
            CultureInfo.InvariantCulture,
            $"SET LOCAL statement_timeout = {captured.StatementTimeout};"
        );
        commandText.AppendLine(CultureInfo.InvariantCulture, $"SET LOCAL lock_timeout = {captured.LockTimeout};");

        command.SetCommandText(commandText.ToString());
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static bool _UseTransactionScopedLock(DatabaseConnection connection) =>
        // Transaction-scoped locking applies to internally-owned connections with a transaction and externally-owned
        // connections whose transaction we can see (i.e. came through the transactional API).
        connection.HasTransaction;

    [StructLayout(LayoutKind.Auto)]
    private readonly struct CapturedTimeoutSettings(string statementTimeout, string lockTimeout)
    {
        public int StatementTimeout { get; } = _ParsePostgresTimeout(statementTimeout);

        public int LockTimeout { get; } = _ParsePostgresTimeout(lockTimeout);

        private static int _ParsePostgresTimeout(string timeout) =>
            PostgresTimeoutRegex.Match(timeout) is { Success: true, Value: var value }
                ? int.Parse(value, CultureInfo.InvariantCulture)
                : throw new FormatException($"Unexpected timeout setting value '{timeout}'.");
    }

    [GeneratedRegex(@"^\d+(?=(?:ms)?$)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PostgresTimeoutRegex { get; }
}
