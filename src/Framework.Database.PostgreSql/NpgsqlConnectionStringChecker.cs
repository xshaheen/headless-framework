using Microsoft.Extensions.Logging;
using Npgsql;

namespace Framework.Database.PostgreSql;

[PublicAPI]
public sealed class NpgsqlConnectionStringChecker(ILogger<NpgsqlConnectionStringChecker> logger)
    : IConnectionStringChecker
{
    public async Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString)
    {
        var result = (Connected: false, DatabaseExists: false);

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 1 };

        var oldDatabaseName = connectionBuilder.Database;
        connectionBuilder.Database = "postgres";

        try
        {
            await using var conn = new NpgsqlConnection(connectionBuilder.ConnectionString);

            await conn.OpenAsync();
            result.Connected = true;

            await conn.ChangeDatabaseAsync(oldDatabaseName!);
            result.DatabaseExists = true;

            await conn.CloseAsync();

            return result;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Error while checking connection string");

            return result;
        }
    }
}
