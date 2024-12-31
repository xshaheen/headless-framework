using Framework.ResourceLocks.Storage.RegularLocks;
using Microsoft.Data.Sqlite;

namespace Tests.TestSetup;

public sealed class SqliteResourceLockStorage(SqliteConnection connection) : IResourceLockStorage
{
    public async ValueTask CreateTableAsync()
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ResourceLocks (
                Key TEXT PRIMARY KEY,
                Value TEXT,
                Expiration INTEGER DEFAULT NULL
            )
            """;

        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? expiration = null)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM ResourceLocks WHERE Expiration < @now;
            INSERT INTO ResourceLocks (Key, Value, Expiration)
                               VALUES (@key, @value, @expiration)
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@value", lockId));
        command.Parameters.Add(new SqliteParameter("@expiration", expiration?.ToString("c")));

        var result = await command.ExecuteNonQueryAsync() > 0;

        return result;
    }

    public async ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string lockId,
        string expected,
        TimeSpan? expiration = null
    )
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE ResourceLocks
            SET Value = @value, Expiration = @expiration
            WHERE Key = @key AND Value = @expected
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@value", lockId));
        command.Parameters.Add(new SqliteParameter("@expected", expected));
        command.Parameters.Add(new SqliteParameter("@expiration", expiration?.ToString("c")));

        var result = await command.ExecuteNonQueryAsync() > 0;

        return result;
    }

    public async ValueTask<bool> RemoveIfEqualAsync(string key, string lockId)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM ResourceLocks
            WHERE Key = @key AND Value = @value
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@value", lockId));

        var result = await command.ExecuteNonQueryAsync() > 0;

        return result;
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Expiration FROM ResourceLocks WHERE Key = @key";
        command.Parameters.Add(new SqliteParameter("@key", key));

        var result = await command.ExecuteScalarAsync();

        if (result is null)
        {
            return null;
        }

        var expiration = TimeSpan.ParseExact(result.ToString(), "c", CultureInfo.InvariantCulture);

        return expiration;
    }

    public async ValueTask<bool> ExistsAsync(string key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM ResourceLocks WHERE Key = @key";
        command.Parameters.Add(new SqliteParameter("@key", key));
        var result = await command.ExecuteScalarAsync();

        var exists = Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;

        return exists;
    }
}
