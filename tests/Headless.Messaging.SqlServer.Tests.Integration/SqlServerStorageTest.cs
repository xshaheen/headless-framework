using Dapper;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerStorageTest(SqlServerTestFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        var storage = _GetStorageInitializer();
        await storage.InitializeAsync();
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

    [Theory]
    [InlineData("messaging.published")]
    [InlineData("messaging.received")]
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
        result.Should().NotBeNull();
        result.Should().Be(table);
    }

    private IStorageInitializer _GetStorageInitializer()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<SqlServerOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IStorageInitializer>();
    }
}
