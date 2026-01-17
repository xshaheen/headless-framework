using Dapper;

namespace Tests;

[Collection("PostgreSql")]
public class PostgreSqlStorageTest : DatabaseTestHost
{
    private readonly string _dbName = ConnectionUtil.GetDatabaseName();
    private readonly string _masterDbConnectionString = ConnectionUtil.GetMasterConnectionString();

    [Fact]
    public void Database_IsExists()
    {
        using var connection = ConnectionUtil.CreateConnection(_masterDbConnectionString);

        var databaseName = ConnectionUtil.GetDatabaseName();
        var sql = $@"SELECT datname FROM pg_database WHERE datname = '{databaseName}'";
        var result = connection.QueryFirstOrDefault<string>(sql);
        result.Should().NotBeNull();
        databaseName.Equals(result, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Theory]
    [InlineData("cap.published")]
    [InlineData("cap.received")]
    public void DatabaseTable_IsExists(string tableName)
    {
        using var connection = ConnectionUtil.CreateConnection(_masterDbConnectionString);

        var parts = tableName.Split('.');
        var schema = parts[0];
        var table = parts[1];

        var sql = $"""
            SELECT table_name FROM information_schema.tables
            WHERE table_catalog='{_dbName}' AND table_schema = '{schema}' AND table_name = '{table}'
            """;

        var result = connection.QueryFirstOrDefault<string>(sql);
        result.Should().NotBeNull();
        result.Should().Be(table);
    }
}
