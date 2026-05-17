using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixtures;

namespace Tests.Fixture;

[CollectionDefinition(DisableParallelization = true)]
public sealed class HeadlessDbContextTestFixture
    : PostgreSqlDbContextTestFixture<TestHeadlessDbContext>,
        ICollectionFixture<HeadlessDbContextTestFixture>
{
    public static Faker Faker { get; } = new();

    protected override void ConfigureDbContext(IServiceCollection services)
    {
        services.AddDbContext<TestHeadlessDbContext>(options =>
            options.UseNpgsql(SqlConnectionString).AddHeadlessExtension()
        );
    }
}
