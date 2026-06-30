// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // SQL text is fixed except lock owner, selected from Session/Transaction constants.
/// <summary>
/// Low-level helpers that issue <c>sys.sp_getapplock</c> / <c>sys.sp_releaseapplock</c> and
/// <c>APPLOCK_MODE</c> T-SQL calls on behalf of the SQL Server distributed-lock provider.
/// All acquire paths encode the resource before calling these helpers.
/// </summary>
internal static class SqlServerApplicationLock
{
    /// <summary>Lock mode string for exclusive (writer) locks passed to <c>sp_getapplock @LockMode</c>.</summary>
    public const string ExclusiveLockMode = "Exclusive";

    /// <summary>Lock mode string for shared (reader) locks passed to <c>sp_getapplock @LockMode</c>.</summary>
    public const string SharedLockMode = "Shared";

    /// <summary>
    /// Attempts to acquire a session-scoped application lock on <paramref name="resource"/> using
    /// <c>sys.sp_getapplock @LockOwner = 'Session'</c>. The lock survives transaction boundaries and is
    /// released explicitly via <see cref="ReleaseSessionAsync"/> or when the connection closes.
    /// On cancellation, the lock is released before the <see cref="OperationCanceledException"/> propagates.
    /// </summary>
    /// <param name="connection">Open SQL Server connection on which to execute the lock command.</param>
    /// <param name="resource">Encoded resource name (must already be within the 255-character limit).</param>
    /// <param name="isShared"><see langword="true"/> for a shared lock; <see langword="false"/> for exclusive.</param>
    /// <param name="acquireTimeout">
    /// Maximum time to wait for the lock. Converted to milliseconds and passed as <c>@LockTimeout</c>.
    /// <see cref="Timeout.InfiniteTimeSpan"/> maps to <c>-1</c> (wait forever).
    /// <see cref="TimeSpan.Zero"/> or negative maps to <c>0</c> (try-once, no wait).
    /// </param>
    /// <param name="commandTimeout">ADO.NET command timeout for the SQL command.</param>
    /// <param name="cancellationToken">Token used to cancel the acquisition attempt.</param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if the resource is held in
    /// a conflicting mode and the acquire timeout was reached (including re-entrant attempts with a
    /// non-infinite timeout, which return <see langword="false"/> rather than blocking).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when SQL Server rejects the resource name or lock mode parameters (<c>sp_getapplock</c> returns -999).</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a re-entrant infinite wait would deadlock (code 103 with infinite timeout), when SQL
    /// Server returns an unsupported upgradeable lock mode (code 104), or when an unexpected
    /// <c>sp_getapplock</c> return code is received.
    /// </exception>
    /// <exception cref="DistributedLockDeadlockException">Thrown when SQL Server detects a deadlock (<c>sp_getapplock</c> returns -3).</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires or SQL Server cancels the wait (<c>sp_getapplock</c> returns -2).</exception>
    public static async ValueTask<bool> TryAcquireSessionAsync(
        SqlConnection connection,
        string resource,
        bool isShared,
        TimeSpan acquireTimeout,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await _ExecuteAcquireAsync(
                    connection,
                    transaction: null,
                    resource,
                    isShared,
                    lockOwner: "Session",
                    acquireTimeout,
                    commandTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return MapAcquireResult(resource, result, acquireTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await ReleaseSessionAsync(connection, resource, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Attempts to acquire a transaction-scoped application lock on <paramref name="resource"/> using
    /// <c>sys.sp_getapplock @LockOwner = 'Transaction'</c>. The lock is automatically released when
    /// <paramref name="transaction"/> commits or rolls back.
    /// On cancellation, the lock is released before the <see cref="OperationCanceledException"/> propagates.
    /// </summary>
    /// <param name="transaction">
    /// Active SQL Server transaction that will own the lock. Its associated connection must be open.
    /// </param>
    /// <param name="resource">Encoded resource name (must already be within the 255-character limit).</param>
    /// <param name="isShared"><see langword="true"/> for a shared lock; <see langword="false"/> for exclusive.</param>
    /// <param name="acquireTimeout">
    /// Maximum time to wait for the lock. Converted to milliseconds and passed as <c>@LockTimeout</c>.
    /// <see cref="Timeout.InfiniteTimeSpan"/> maps to <c>-1</c> (wait forever).
    /// <see cref="TimeSpan.Zero"/> or negative maps to <c>0</c> (try-once, no wait).
    /// </param>
    /// <param name="commandTimeout">ADO.NET command timeout for the SQL command.</param>
    /// <param name="cancellationToken">Token used to cancel the acquisition attempt.</param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if the resource is held in
    /// a conflicting mode and the acquire timeout was reached.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="transaction"/> has no associated open connection (already committed,
    /// rolled back, or disposed), when a re-entrant infinite wait would deadlock (code 103), when SQL
    /// Server returns an unsupported upgradeable lock mode (code 104), or when an unexpected
    /// <c>sp_getapplock</c> return code is received.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when SQL Server rejects the resource name or lock mode parameters (<c>sp_getapplock</c> returns -999).</exception>
    /// <exception cref="DistributedLockDeadlockException">Thrown when SQL Server detects a deadlock (<c>sp_getapplock</c> returns -3).</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires or SQL Server cancels the wait (<c>sp_getapplock</c> returns -2).</exception>
    public static async ValueTask<bool> TryAcquireTransactionAsync(
        SqlTransaction transaction,
        string resource,
        bool isShared,
        TimeSpan acquireTimeout,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken = default
    )
    {
        var connection =
            transaction.Connection
            ?? throw new InvalidOperationException(
                "The transaction has no associated open connection (already committed, rolled back, or disposed)."
            );

        try
        {
            var result = await _ExecuteAcquireAsync(
                    connection,
                    transaction,
                    resource,
                    isShared,
                    lockOwner: "Transaction",
                    acquireTimeout,
                    commandTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return MapAcquireResult(resource, result, acquireTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _ReleaseTransactionAsync(connection, transaction, resource, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Translates a raw <c>sp_getapplock</c> return code into a Boolean success flag or the appropriate
    /// exception. Return codes and their meanings: 0/1 = acquired; -1 = timeout (returns
    /// <see langword="false"/>); -2 = cancelled; -3 = deadlock; -999 = invalid parameters; 103 =
    /// re-entrant (returns <see langword="false"/> unless the acquire timeout is infinite, which would
    /// deadlock); 104 = unsupported mode.
    /// </summary>
    /// <param name="resource">Resource name, used in exception messages.</param>
    /// <param name="result">Raw integer returned by <c>sp_getapplock</c>.</param>
    /// <param name="acquireTimeout">Acquire timeout in effect; used to detect infinite re-entrant waits.</param>
    /// <param name="cancellationToken">Token embedded into the <see cref="OperationCanceledException"/> when result is -2.</param>
    /// <returns><see langword="true"/> if the lock was acquired (result ≥ 0, excluding re-entrant cases); <see langword="false"/> if the resource is contended and the timeout elapsed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when result is -2 (SQL Server cancelled the wait).</exception>
    /// <exception cref="DistributedLockDeadlockException">Thrown when result is -3 (deadlock detected).</exception>
    /// <exception cref="ArgumentException">Thrown when result is -999 (invalid resource or mode parameters).</exception>
    /// <exception cref="InvalidOperationException">Thrown when result is 103 with an infinite acquire timeout (re-entrant infinite wait), result is 104 (unsupported mode), or result is any other unrecognized negative value.</exception>
    internal static bool MapAcquireResult(
        string resource,
        int result,
        TimeSpan acquireTimeout,
        CancellationToken cancellationToken = default
    )
    {
        return result switch
        {
            -1 => false,
            -2 => throw new OperationCanceledException(
                $"SQL Server cancelled distributed lock acquisition for '{resource}'.",
                cancellationToken
            ),
            -3 => throw new DistributedLockDeadlockException(resource),
            -999 => throw new ArgumentException(
                $"SQL Server rejected distributed lock resource '{resource}' or lock mode parameters.",
                nameof(resource)
            ),
            103 when acquireTimeout == Timeout.InfiniteTimeSpan => throw new InvalidOperationException(
                $"SQL Server connection already holds distributed lock '{resource}'; an infinite re-entrant wait would hang."
            ),
            103 => false,
            104 => throw new InvalidOperationException(
                $"SQL Server rejected unsupported upgradeable distributed lock mode for '{resource}'."
            ),
            >= 0 => true,
            _ => throw new InvalidOperationException(
                $"SQL Server returned unexpected sp_getapplock result {result} for distributed lock '{resource}'."
            ),
        };
    }

    /// <summary>
    /// Releases the session-scoped application lock on <paramref name="resource"/> via
    /// <c>sys.sp_releaseapplock @LockOwner = 'Session'</c>. A no-op when the connection is not open or
    /// the lock is not currently held (<c>APPLOCK_MODE</c> is <c>NoLock</c>).
    /// </summary>
    /// <param name="connection">The SQL Server connection that holds the session-scoped lock.</param>
    /// <param name="resource">Encoded resource name to release.</param>
    /// <param name="cancellationToken">Token used to cancel the release command.</param>
    /// <remarks>Underlying <see cref="Microsoft.Data.SqlClient.SqlException"/> errors from the release command propagate to the caller.</remarks>
    public static async ValueTask ReleaseSessionAsync(
        SqlConnection connection,
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF APPLOCK_MODE(N'public', @resource, N'Session') <> N'NoLock'
                EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = N'Session', @DbPrincipal = N'public';
            """;
        command.Parameters.AddWithValue(nameof(resource), resource);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask _ReleaseTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string resource,
        CancellationToken cancellationToken
    )
    {
        if (connection.State != System.Data.ConnectionState.Open || transaction.Connection is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF APPLOCK_MODE(N'public', @resource, N'Transaction') <> N'NoLock'
                EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = N'Transaction', @DbPrincipal = N'public';
            """;
        command.Parameters.AddWithValue(nameof(resource), resource);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> _ExecuteAcquireAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string resource,
        bool isShared,
        string lockOwner,
        TimeSpan acquireTimeout,
        TimeSpan commandTimeout,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = GetCommandTimeoutSeconds(acquireTimeout, commandTimeout);
        command.CommandText = $$"""
            DECLARE @result int;

            IF APPLOCK_MODE(N'public', @resource, N'{{lockOwner}}') <> N'NoLock'
                SELECT CAST(103 AS int);
            ELSE
            BEGIN
                EXEC @result = sys.sp_getapplock
                    @Resource = @resource,
                    @LockMode = @lockMode,
                    @LockOwner = N'{{lockOwner}}',
                    @LockTimeout = @lockTimeout,
                    @DbPrincipal = N'public';
                SELECT @result;
            END;
            """;
        command.Parameters.AddWithValue(nameof(resource), resource);
        command.Parameters.AddWithValue("lockMode", isShared ? SharedLockMode : ExclusiveLockMode);
        command.Parameters.AddWithValue("lockTimeout", _ToLockTimeoutMilliseconds(acquireTimeout));

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>
    /// Converts <paramref name="commandTimeout"/> to an ADO.NET whole-second budget, clamping sub-second
    /// values up to 1 so the result never collapses to 0 (ADO's infinite-wait sentinel).
    /// </summary>
    /// <param name="commandTimeout">Desired command timeout.</param>
    /// <returns>
    /// The ceiling of <paramref name="commandTimeout"/> in whole seconds, clamped to at least 1 and at
    /// most <see cref="int.MaxValue"/>.
    /// </returns>
    internal static int GetCommandTimeoutSeconds(TimeSpan commandTimeout)
    {
        return commandTimeout.TotalSeconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(commandTimeout.TotalSeconds));
    }

    /// <summary>
    /// Computes the ADO.NET command timeout for a lock-acquire command, ensuring the command deadline
    /// encompasses the full <c>sp_getapplock</c> blocking window. When <paramref name="acquireTimeout"/>
    /// is <see cref="Timeout.InfiniteTimeSpan"/>, returns 0 (ADO's infinite sentinel). Otherwise, the
    /// effective command timeout is the larger of <paramref name="commandTimeout"/> and
    /// <paramref name="acquireTimeout"/> plus one second of overhead.
    /// </summary>
    /// <param name="acquireTimeout">
    /// Maximum lock-wait time passed to <c>sp_getapplock @LockTimeout</c>. When
    /// <see cref="Timeout.InfiniteTimeSpan"/>, the ADO command must also wait indefinitely (returns 0).
    /// </param>
    /// <param name="commandTimeout">
    /// Minimum ADO command timeout floor; applied when it exceeds the acquire timeout.
    /// </param>
    /// <returns>ADO.NET command timeout in whole seconds; 0 when infinite.</returns>
    internal static int GetCommandTimeoutSeconds(TimeSpan acquireTimeout, TimeSpan commandTimeout)
    {
        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            return 0;
        }

        var effectiveTimeout =
            acquireTimeout <= TimeSpan.Zero ? commandTimeout
            : commandTimeout > acquireTimeout ? commandTimeout
            : acquireTimeout + TimeSpan.FromSeconds(1);

        return effectiveTimeout.TotalSeconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(effectiveTimeout.TotalSeconds));
    }

    private static int _ToLockTimeoutMilliseconds(TimeSpan acquireTimeout)
    {
        if (acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            return -1;
        }

        if (acquireTimeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return acquireTimeout.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(acquireTimeout.TotalMilliseconds);
    }
}
#pragma warning restore CA2100
