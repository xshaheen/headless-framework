// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

#pragma warning disable CA2100 // SQL text is fixed except lock owner, selected from Session/Transaction constants.
internal static class SqlServerApplicationLock
{
    public const string ExclusiveLockMode = "Exclusive";
    public const string SharedLockMode = "Shared";

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
        command.Parameters.AddWithValue("resource", resource);
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
        command.Parameters.AddWithValue("resource", resource);
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
        command.Parameters.AddWithValue("resource", resource);
        command.Parameters.AddWithValue("lockMode", isShared ? SharedLockMode : ExclusiveLockMode);
        command.Parameters.AddWithValue("lockTimeout", _ToLockTimeoutMilliseconds(acquireTimeout));

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>
    /// Converts a plain command timeout to ADO's whole-second budget, clamping sub-second values up to 1 so they do
    /// not collapse to 0 (ADO's infinite-wait sentinel).
    /// </summary>
    internal static int GetCommandTimeoutSeconds(TimeSpan commandTimeout)
    {
        return commandTimeout.TotalSeconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(commandTimeout.TotalSeconds));
    }

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
