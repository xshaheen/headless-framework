using Npgsql;

namespace Tests;

public static class ConnectionUtil
{
    private const string _ConnectionStringTemplateVariable = "Cap_PostgreSql_ConnectionString";

    private const string _MasterDatabaseName = "postgres";
    private const string _DefaultDatabaseName = "cap_test";

    private const string _DefaultConnectionString =
        @"Host=localhost;Database=cap_test;Username=postgres;Password=postgres";

    public static string GetDatabaseName()
    {
        return _DefaultDatabaseName;
    }

    public static string GetMasterConnectionString()
    {
        return GetConnectionString().Replace(_DefaultDatabaseName, _MasterDatabaseName);
    }

    public static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable(_ConnectionStringTemplateVariable) ?? _DefaultConnectionString;
    }

    public static NpgsqlConnection CreateConnection(string? connectionString = null)
    {
        connectionString ??= GetConnectionString();
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }
}
