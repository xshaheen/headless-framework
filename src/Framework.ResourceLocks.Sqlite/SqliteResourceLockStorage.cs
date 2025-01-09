// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks.Storage.RegularLocks;
using Microsoft.Data.Sqlite;

namespace Framework.ResourceLocks.Sqlite;

public sealed class SqliteResourceLockStorage(SqliteConnection connection) : IResourceLockStorage
{
    private const string _CreateTableSql = """
        CREATE TABLE IF NOT EXISTS ResourceLocks (
            res TEXT PRIMARY KEY,
            lockId TEXT,
            exp INTEGER DEFAULT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_expiration ON ResourceLocks (exp)
        """;

    private const string _InsertSql = """
        DELETE FROM ResourceLocks WHERE exp < (select strftime('%s','now') - 7200);
        INSERT INTO ResourceLocks (res, lockId, exp) VALUES (@res, @lockId, (select strftime('%s','now') + @exp))
        """;

    private const string _ReplaceIfEqualSql = """
        UPDATE ResourceLocks
        SET lockId = @lockId, exp = (select strftime('%s','now') + @exp)
        WHERE res = @res AND lockId = @expected
        """;

    private const string _GetExpirationSql = """
        SELECT exp FROM ResourceLocks
        WHERE res = @res AND exp > (select strftime('%s','now'))
        """;

    private const string _IsExistSql = """
        SELECT COUNT(1) FROM ResourceLocks
        WHERE res = @res AND exp > (select strftime('%s','now'))
        """;

    private const string _RemoveSql = "DELETE FROM ResourceLocks WHERE res = @res AND lockId = @lockId";

    public void CreateTable()
    {
        using var command = connection.CreateCommand();

        command.CommandText = _CreateTableSql;
        command.ExecuteNonQuery();
    }

    public async ValueTask CreateTableAsync()
    {
        await using var command = connection.CreateCommand();

        command.CommandText = _CreateTableSql;
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = _InsertSql;
        command.Parameters.Add(new SqliteParameter("@res", key));
        command.Parameters.Add(new SqliteParameter("@lockId", lockId));
        command.Parameters.Add(new SqliteParameter("@exp", _GetTimeSpanParameterValue(ttl)));

        try
        {
            return await command.ExecuteNonQueryAsync() > 0;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) // Unique constraint violation
        {
            return false;
        }
    }

    public async ValueTask<bool> ReplaceIfEqualAsync(string key, string lockId, string expected, TimeSpan? ttl = null)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = _ReplaceIfEqualSql;
        command.Parameters.Add(new SqliteParameter("@res", key));
        command.Parameters.Add(new SqliteParameter("@lockId", lockId));
        command.Parameters.Add(new SqliteParameter("@expected", expected));
        command.Parameters.Add(new SqliteParameter("@exp", _GetTimeSpanParameterValue(ttl)));

        var result = await command.ExecuteNonQueryAsync() > 0;

        return result;
    }

    public async ValueTask<bool> RemoveAsync(string key, string lockId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _RemoveSql;
        command.Parameters.Add(new SqliteParameter("@res", key));
        command.Parameters.Add(new SqliteParameter("@lockId", lockId));

        var result = await command.ExecuteNonQueryAsync() > 0;

        return result;
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _GetExpirationSql;
        command.Parameters.Add(new SqliteParameter("@res", key));

        var result = (await command.ExecuteScalarAsync())?.ToString();

        if (result is null)
        {
            return null;
        }

        var seconds = long.Parse(result, CultureInfo.InvariantCulture);
        var expiration = TimeSpan.FromSeconds(seconds);

        return expiration;
    }

    public async ValueTask<bool> ExistsAsync(string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _IsExistSql;
        command.Parameters.Add(new SqliteParameter("@res", key));
        var result = await command.ExecuteScalarAsync();

        var exists = Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;

        return exists;
    }

    #region Helpers

    private static object _GetTimeSpanParameterValue(TimeSpan? timeSpan)
    {
        return timeSpan is null ? DBNull.Value : _GetSeconds(timeSpan.Value);
    }

    private static long _GetSeconds(TimeSpan ttl) => ttl.Seconds / 10_000_000;

    #endregion
}
