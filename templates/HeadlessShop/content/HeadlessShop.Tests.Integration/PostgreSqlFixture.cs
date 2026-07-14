using Testcontainers.PostgreSql;

namespace HeadlessShop.Tests.Integration;

[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlFixture : ICollectionFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("headless_shop_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => new(_container.DisposeAsync().AsTask());
}
