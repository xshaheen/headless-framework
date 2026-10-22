// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Seeders;

public sealed class SeedersTests : TestBase
{
    [Fact]
    public async Task should_run_seeders_in_registration_order_when_none_depends_on_another()
    {
        // given
        var recorder = new SeedRecorder();
        await using var provider = _Build(recorder, s => s.AddSeeder<SecondSeeder>().AddSeeder<FirstSeeder>());

        // when
        await provider.SeedAsync(AbortToken);

        // then
        recorder.Ran.Should().Equal(nameof(SecondSeeder), nameof(FirstSeeder));
    }

    [Fact]
    public async Task should_run_a_dependency_first_when_it_is_registered_after_its_dependent()
    {
        // given: the dependent is registered first, so only DependsOn can put the dependency ahead of it
        var recorder = new SeedRecorder();
        await using var provider = _Build(recorder, s => s.AddSeeder<DependentSeeder>().AddSeeder<FirstSeeder>());

        // when
        await provider.SeedAsync(AbortToken);

        // then
        recorder.Ran.Should().Equal(nameof(FirstSeeder), nameof(DependentSeeder));
    }

    [Fact]
    public async Task should_ignore_a_dependency_that_is_not_registered()
    {
        // given
        var recorder = new SeedRecorder();
        await using var provider = _Build(recorder, s => s.AddSeeder<DependentSeeder>());

        // when
        await provider.SeedAsync(AbortToken);

        // then
        recorder.Ran.Should().Equal(nameof(DependentSeeder));
    }

    [Fact]
    public async Task should_fail_naming_the_cycle_when_two_seeders_depend_on_each_other()
    {
        // given
        var recorder = new SeedRecorder();
        await using var provider = _Build(recorder, s => s.AddSeeder<CycleASeeder>().AddSeeder<CycleBSeeder>());

        // when
        var act = () => provider.SeedAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nameof(CycleASeeder)} -> {nameof(CycleBSeeder)} -> {nameof(CycleASeeder)}*");
        recorder.Ran.Should().BeEmpty("no seeder runs when the order cannot be resolved");
    }

    [Fact]
    public async Task should_stop_at_the_first_failure_and_keep_its_error_as_the_inner_exception()
    {
        // given
        var recorder = new SeedRecorder();
        await using var provider = _Build(recorder, s => s.AddSeeder<FailingSeeder>().AddSeeder<FirstSeeder>());

        // when
        var act = () => provider.SeedAsync(AbortToken);

        // then
        (
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{nameof(FailingSeeder)}*")
        ).WithInnerException<TimeoutException>();
        recorder.Ran.Should().BeEmpty("a later seeder may need what the failed one writes");
    }

    private static ServiceProvider _Build(SeedRecorder recorder, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        register(services);

        return services.BuildServiceProvider();
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

    private sealed class FirstSeeder(SeedRecorder recorder) : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken)
        {
            recorder.Record(nameof(FirstSeeder));

            return Task.CompletedTask;
        }
    }

    private sealed class SecondSeeder(SeedRecorder recorder) : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken)
        {
            recorder.Record(nameof(SecondSeeder));

            return Task.CompletedTask;
        }
    }

    private sealed class DependentSeeder(SeedRecorder recorder) : ISeeder
    {
        public IEnumerable<ISeeder> DependsOn(IReadOnlyList<ISeeder> seeders) => seeders.OfType<FirstSeeder>();

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            recorder.Record(nameof(DependentSeeder));

            return Task.CompletedTask;
        }
    }

    private sealed class CycleASeeder(SeedRecorder recorder) : ISeeder
    {
        public IEnumerable<ISeeder> DependsOn(IReadOnlyList<ISeeder> seeders) => seeders.OfType<CycleBSeeder>();

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            recorder.Record(nameof(CycleASeeder));

            return Task.CompletedTask;
        }
    }

    private sealed class CycleBSeeder(SeedRecorder recorder) : ISeeder
    {
        public IEnumerable<ISeeder> DependsOn(IReadOnlyList<ISeeder> seeders) => seeders.OfType<CycleASeeder>();

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            recorder.Record(nameof(CycleBSeeder));

            return Task.CompletedTask;
        }
    }

    private sealed class FailingSeeder : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken) =>
            Task.FromException(new TimeoutException("store unreachable"));
    }
}
