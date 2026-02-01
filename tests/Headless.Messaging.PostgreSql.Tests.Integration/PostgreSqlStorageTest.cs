using Dapper;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlStorageTest(PostgreSqlTestFixture fixture) : IAsyncLifetime
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
        using var connection = new NpgsqlConnection(fixture.ConnectionString);
        connection.Open();

        var databaseName = "messages_test";
        var sql = $@"SELECT datname FROM pg_database WHERE datname = '{databaseName}'";
        var result = connection.QueryFirstOrDefault<string>(sql);
        result.Should().NotBeNull();
        databaseName.Equals(result, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Theory]
    [InlineData("messaging.published")]
    [InlineData("messaging.received")]
    public void should_create_table(string tableName)
    {
        using var connection = new NpgsqlConnection(fixture.ConnectionString);
        connection.Open();

        var parts = tableName.Split('.');
        var schema = parts[0];
        var table = parts[1];

        var sql = $"""
            SELECT table_name FROM information_schema.tables
            WHERE table_catalog='messages_test' AND table_schema = '{schema}' AND table_name = '{table}'
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
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IStorageInitializer>();
    }
}
