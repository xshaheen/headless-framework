using Headless.Hosting.Seeders;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Seeders;

public sealed class SeedersTests : TestBase
{
    [Fact]
    public async Task should_be_called_once_when_seed_async_seeding()
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

    [Fact]
    public async Task should_run_all_seeders_in_ascending_priority_order_when_seed_async()
    {
        // given — registered in reverse order to prove the runner orders by [SeederPriority]
        var recorder = new SeedRecorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        services.AddSeeder<SecondSeeder>();
        services.AddSeeder<FirstSeeder>();
        var provider = services.BuildServiceProvider();

        // when
        await provider.SeedAsync(cancellationToken: AbortToken);

        // then — both ran, lower priority first
        recorder.Ran.Should().Equal(nameof(FirstSeeder), nameof(SecondSeeder));
    }

    private sealed class SeedRecorder
    {
        private readonly List<string> _ran = [];

        public IReadOnlyList<string> Ran => _ran;

        public void Record(string name)
        {
            _ran.Add(name);
        }
    }

    [SeederPriority(1)]
    private sealed class FirstSeeder(SeedRecorder recorder) : ISeeder
    {
        public ValueTask SeedAsync(CancellationToken cancellationToken = default)
        {
            recorder.Record(nameof(FirstSeeder));

            return ValueTask.CompletedTask;
        }
    }

    [SeederPriority(2)]
    private sealed class SecondSeeder(SeedRecorder recorder) : ISeeder
    {
        public ValueTask SeedAsync(CancellationToken cancellationToken = default)
        {
            recorder.Record(nameof(SecondSeeder));

            return ValueTask.CompletedTask;
        }
    }
}
