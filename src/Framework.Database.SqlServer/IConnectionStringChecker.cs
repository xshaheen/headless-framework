using Microsoft.Data.SqlClient;

namespace Framework.Database.SqlServer;

public interface IConnectionStringChecker
{
    Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString);
}

[PublicAPI]
public sealed class SqlServerConnectionStringChecker : IConnectionStringChecker
{
    public async Task<(bool Connected, bool DatabaseExists)> CheckAsync(string connectionString)
    {
        var result = (Connected: false, DatabaseExists: false);
        var connString = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 1 };

        var oldDatabaseName = connString.InitialCatalog;
        connString.InitialCatalog = "master";

        try
        {
            await using var conn = new SqlConnection(connString.ConnectionString);
            await conn.OpenAsync();
            result.Connected = true;
            await conn.ChangeDatabaseAsync(oldDatabaseName);
            result.DatabaseExists = true;

            await conn.CloseAsync();

            return result;
        }
        catch
        {
#pragma warning disable ERP022 // Exit point 'return result;' swallows an unobserved exception.
            return result;
#pragma warning restore ERP022
        }
    }
}
