// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Framework.ResourceLocks.Sqlite;

public sealed class SqliteThrottlingResourceLockStorage(SqliteConnection connection, TimeProvider timeProvider)
    : IThrottlingResourceLockStorage
{
    private readonly TimeSpan _clearExpiredInterval = TimeSpan.FromMinutes(5);
    private const int _PeriodsToKeep = 10;
    private long _lastClearExpired;

    private const string _CreateTable = """
        CREATE TABLE IF NOT EXISTS ThrottlingLocks (
            res TEXT PRIMARY KEY,
            hits INTEGER DEFAULT 0,
            exp INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_expiration ON ThrottlingLocks (exp);
        """;

    private const string _IncrementSql = """
        INSERT INTO ThrottlingLocks (res, hits, exp) VALUES (@res, 1, (select strftime('%s','now') + @exp))
        ON CONFLICT(res) DO UPDATE SET hits = hits + 1
        RETURNING hits
        """;

    private const string _DeleteExpiredSql = """
        DELETE FROM ThrottlingLocks
        WHERE exp < (select strftime('%s','now') - @period);
        """;

    private const string _GetHitsSql = "SELECT hits FROM ThrottlingLocks WHERE res = @res";

    /// <summary>Creates the ThrottlingLocks table if it does not already exist.</summary>
    public void CreateTable()
    {
        using var command = connection.CreateCommand();
        command.CommandText = _CreateTable;
        command.ExecuteNonQuery();
    }

    /// <summary>Asynchronously creates the ThrottlingLocks table if it does not already exist.</summary>
    public async ValueTask CreateTableAsync()
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _CreateTable;
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask<long> GetHitCountsAsync(string resource, long defaultValue = 0)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = _GetHitsSql;
        command.Parameters.Add(new SqliteParameter("@res", resource));

        var result = await command.ExecuteScalarAsync();

        if (result is null)
        {
            return defaultValue;
        }

        var value = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        return value;
    }

    public async ValueTask<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        await using var command = connection.CreateCommand();

        _AddClearExpired(command, ttl);
        command.CommandText += _IncrementSql;
        command.Parameters.Add(new SqliteParameter("@res", resource));
        command.Parameters.Add(new SqliteParameter("@exp", _GetSeconds(ttl)));

        var result = await command.ExecuteScalarAsync();
        var value = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        return value;
    }

    #region Helpers

    private void _AddClearExpired(DbCommand command, TimeSpan ttl)
    {
        // If we have cleared expired locks recently, then skip this time.
        var lastClearExpired = Interlocked.Read(ref _lastClearExpired);

        if (lastClearExpired != 0 && timeProvider.GetElapsedTime(lastClearExpired) < _clearExpiredInterval)
        {
            return;
        }

        Interlocked.Exchange(ref _lastClearExpired, timeProvider.GetTimestamp());
        command.CommandText = _DeleteExpiredSql;
        command.Parameters.Add(new SqliteParameter("@period", _GetSeconds(ttl) * _PeriodsToKeep));
    }

    private static long _GetSeconds(TimeSpan ttl) => ttl.Seconds / 10_000_000;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #endregion
}
