using Microsoft.Data.Sqlite;

namespace Framework.Database.Sqlite;

[PublicAPI]
public sealed class SqliteConnectionStringChecker : IConnectionStringChecker
{
    public async Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString)
    {
        var result = (Connected: false, DatabaseExists: false);

        try
        {
            await using var connection = new SqliteConnection(connectionString);

            await connection.OpenAsync();
            result.Connected = true;
            result.DatabaseExists = true;
            await connection.CloseAsync();

            return result;
        }
        catch (Exception)
        {
            return result;
        }
    }
}
