using System.Data;
using Framework.ResourceLocks.Storage.RegularLocks;
using Microsoft.Data.Sqlite;

namespace Tests.TestSetup;

public sealed class SqliteResourceLockStorage(IDbConnection connection) : IResourceLockStorage
{
    public ValueTask<bool> InsertAsync(string key, string value, TimeSpan? expiration = null)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO ResourceLocks (Key, Value, Expiration)
                               VALUES (@key, @value, @expiration)
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@value", value));
        command.Parameters.Add(new SqliteParameter("@expiration", expiration?.ToString("c")));

        var result = command.ExecuteNonQuery() > 0;

        return ValueTask.FromResult(result);
    }

    public ValueTask<bool> ReplaceIfEqualAsync<T>(string key, T value, T expected, TimeSpan? expiration = null)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE ResourceLocks
            SET Value = @value, Expiration = @expiration
            WHERE Key = @key AND Value = @expected
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@value", value));
        command.Parameters.Add(new SqliteParameter("@expected", expected));
        command.Parameters.Add(new SqliteParameter("@expiration", expiration?.ToString("c")));

        var result = command.ExecuteNonQuery() > 0;

        return ValueTask.FromResult(result);
    }

    public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM ResourceLocks
            WHERE Key = @key AND Value = @value
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@value", value));

        var result = command.ExecuteNonQuery() > 0;

        return ValueTask.FromResult(result);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Expiration FROM ResourceLocks WHERE Key = @key";
        command.Parameters.Add(new SqliteParameter("@key", key));

        var result = command.ExecuteScalar();

        if (result is null)
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        var expiration = TimeSpan.ParseExact(result.ToString(), "c", CultureInfo.InvariantCulture);

        return ValueTask.FromResult<TimeSpan?>(expiration);
    }

    public ValueTask<bool> ExistsAsync(string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM ResourceLocks WHERE Key = @key";
        command.Parameters.Add(new SqliteParameter("@key", key));
        var result = command.ExecuteScalar();

        var exists = Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;

        return ValueTask.FromResult(exists);
    }
}
