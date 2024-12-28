using System.Data;
using Framework.ResourceLocks;
using Microsoft.Data.Sqlite;

namespace Tests.TestSetup;

public sealed class SqliteThrottlingResourceLockStorage(IDbConnection connection) : IThrottlingResourceLockStorage
{
    public void CreateTable()
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE ThrottlingResourceLocks (
                Key TEXT PRIMARY KEY,
                Value INTEGER DEFAULT 0,
                Expiration TEXT
            )
            """;

        command.ExecuteNonQuery();
    }

    public ValueTask<long> GetHitCountsAsync(string resources, long defaultValue = 0)
    {
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT Value FROM ThrottlingResourceLocks WHERE Key = @key";
        command.Parameters.Add(new SqliteParameter("@key", resources));

        var result = command.ExecuteScalar();

        if (result is null)
        {
            return ValueTask.FromResult(defaultValue);
        }

        var value = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        return ValueTask.FromResult(value);
    }

    public ValueTask<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO ThrottlingResourceLocks (Key, Value, Expiration) VALUES (@key, 1, @expiration)
            ON CONFLICT(Key) DO UPDATE SET Value = Value + 1, Expiration = @expiration
            RETURNING Value
            """;

        command.Parameters.Add(new SqliteParameter("@key", resource));
        command.Parameters.Add(new SqliteParameter("@expiration", ttl.ToString("c")));

        var result = command.ExecuteScalar();
        var value = Convert.ToInt64(result, CultureInfo.InvariantCulture);

        return ValueTask.FromResult(value);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
