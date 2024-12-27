using Framework.Hosting.Seeders;

namespace Tests.Seeders;

public class SeedersTests
{
    [Fact]
        public async Task seed_async_should_be_called_once_when_seeding()
        {
            // given
            var seeder = Substitute.For<ISeeder>();
            var serviceProvider = Substitute.For<IServiceProvider>();

            serviceProvider.GetService(typeof(ISeeder)).Returns(seeder);

            // when
            await seeder.SeedAsync();

            // then
            await seeder.Received(1).SeedAsync();
        }
}
