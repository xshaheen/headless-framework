using Dapper;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Persistence;
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
        using var connection = new SqlConnection(fixture.Container.GetConnectionString());
        connection.Open();

        var databaseName = "master";
        var sql = "SELECT DB_NAME()";
        var result = connection.QueryFirstOrDefault<string>(sql);
        result.Should().NotBeNull();
        databaseName.Equals(result, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Theory]
    [InlineData("messaging.published")]
    [InlineData("messaging.received")]
    public void should_create_table(string tableName)
    {
        using var connection = new SqlConnection(fixture.Container.GetConnectionString());
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
        services.Configure<SqlServerOptions>(x => x.ConnectionString = fixture.Container.GetConnectionString());
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IStorageInitializer>();
    }
}
