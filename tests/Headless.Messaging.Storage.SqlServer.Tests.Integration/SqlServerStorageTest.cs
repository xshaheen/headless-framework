using Dapper;
using Microsoft.Data.SqlClient;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageTest(SqlServerTestFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        try
        {
            await TestMessagingSchema.ApplyAsync(fixture.ConnectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Initialization failed: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void should_create_database()
    {
        using var connection = new SqlConnection(fixture.ConnectionString);
        connection.Open();

        const string databaseName = "master";
        const string sql = "SELECT DB_NAME()";
        var result = connection.QueryFirstOrDefault<string>(sql);
        result.Should().NotBeNull();
        databaseName.Equals(result, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void should_list_all_tables()
    {
        using var connection = new SqlConnection(fixture.ConnectionString);
        connection.Open();

        const string sql = "SELECT TABLE_SCHEMA + '.' + TABLE_NAME FROM INFORMATION_SCHEMA.TABLES";
        var tables = connection.Query<string>(sql).ToList();

        // Log what tables exist for debugging
        var tableList = string.Join(", ", tables);
        (tables.Count > 0).Should().BeTrue($"No tables found. Available tables: {tableList}");
    }

    [Theory]
    [InlineData("headless.MessagingPublished")]
    [InlineData("headless.MessagingReceived")]
    public void should_create_table(string tableName)
    {
        using var connection = new SqlConnection(fixture.ConnectionString);
        connection.Open();

        var parts = tableName.Split('.');
        var schema = parts[0];
        var table = parts[1];

        var sql = $"""
            SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{table}'
            """;

        var result = connection.QueryFirstOrDefault<string>(sql);
        result.Should().Be(table);
    }
}
