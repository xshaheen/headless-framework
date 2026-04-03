// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Testing.DependencyInjection;
using Headless.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.DependencyInjection;

public sealed class TestTimeProviderServiceCollectionExtensionsTests
{
    [Fact]
    public void should_resolve_fake_time_provider_as_time_provider()
    {
        var services = new ServiceCollection();

        services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<TimeProvider>();

        resolved.Should().BeOfType<FakeTimeProvider>();
    }

    [Fact]
    public void should_resolve_test_clock_as_iclock()
    {
        var services = new ServiceCollection();

        services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IClock>();

        resolved.Should().BeOfType<TestClock>();
    }

    [Fact]
    public void should_return_same_instance_as_resolved_from_di()
    {
        var services = new ServiceCollection();

        var returned = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<TimeProvider>();

        resolved.Should().BeSameAs(returned);
    }

    [Fact]
    public void should_wire_test_clock_with_same_fake_time_provider()
    {
        var services = new ServiceCollection();

        var fakeTimeProvider = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var clock = (TestClock)sp.GetRequiredService<IClock>();

        clock.TimeProvider.Should().BeSameAs(fakeTimeProvider);
    }

    [Fact]
    public void should_reflect_time_advance_in_iclock()
    {
        var services = new ServiceCollection();

        var fakeTimeProvider = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var clock = sp.GetRequiredService<IClock>();

        var before = clock.UtcNow;
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(10));
        var after = clock.UtcNow;

        (after - before).Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void should_replace_production_registrations()
    {
        var services = new ServiceCollection();

        // Simulate production AddTimeService() registration
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock>(new Clock(TimeProvider.System));

        services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<TimeProvider>().Should().BeOfType<FakeTimeProvider>();
        sp.GetRequiredService<IClock>().Should().BeOfType<TestClock>();
    }

    [Fact]
    public void should_work_without_prior_registrations()
    {
        var services = new ServiceCollection();

        var fakeTimeProvider = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<TimeProvider>().Should().BeSameAs(fakeTimeProvider);
        sp.GetRequiredService<IClock>().Should().BeOfType<TestClock>();
    }

    [Fact]
    public void should_replace_ef_only_registration_path()
    {
        var services = new ServiceCollection();

        // EF-only path: IClock registered without TimeProvider
        services.AddSingleton<IClock>(new Clock(TimeProvider.System));

        services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<TimeProvider>().Should().BeOfType<FakeTimeProvider>();
        sp.GetRequiredService<IClock>().Should().BeOfType<TestClock>();
    }
}
