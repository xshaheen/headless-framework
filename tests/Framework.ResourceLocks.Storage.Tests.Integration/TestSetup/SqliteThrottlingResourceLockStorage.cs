using System.Data;
using Framework.ResourceLocks.Storage.ThrottlingLocks;
using Microsoft.Data.Sqlite;

namespace Tests.TestSetup;

public sealed class SqliteThrottlingResourceLockStorage(IDbConnection connection) : IThrottlingResourceLockStorage
{
    public ValueTask<T> GetAsync<T>(string key, T defaultValue)
    {
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT Value FROM ThrottlingResourceLocks WHERE Key = @key";
        command.Parameters.Add(new SqliteParameter("@key", key));

        var result = command.ExecuteScalar();

        if (result is null)
        {
            return ValueTask.FromResult(defaultValue);
        }

        var value = (T)result;

        return ValueTask.FromResult(value);
    }

    public ValueTask<long> IncrementAsync(string key, long amount, TimeSpan? expiration = null)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO ThrottlingResourceLocks (Key, Value, Expiration)
                               VALUES (@key, @amount, @expiration)
            ON CONFLICT(Key) DO UPDATE
            SET Value = Value + @amount, Expiration = @expiration
            RETURNING Value
            """;

        command.Parameters.Add(new SqliteParameter("@key", key));
        command.Parameters.Add(new SqliteParameter("@amount", amount));
        command.Parameters.Add(new SqliteParameter("@expiration", expiration?.ToString("c")));

        var result = command.ExecuteScalar();
        var value = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        return ValueTask.FromResult(value);
    }
}
