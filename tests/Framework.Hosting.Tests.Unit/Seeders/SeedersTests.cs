using Framework.Hosting.Seeders;
using Framework.Testing.Tests;

namespace Tests.Seeders;

public sealed class SeedersTests : TestBase
{
    [Fact]
    public async Task seed_async_should_be_called_once_when_seeding()
    {
        // given
        var seeder = Substitute.For<ISeeder>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        serviceProvider.GetService(typeof(ISeeder)).Returns(seeder);

        // when
        await seeder.SeedAsync(AbortToken);

        // then
        await seeder.Received(1).SeedAsync(AbortToken);
    }
}
