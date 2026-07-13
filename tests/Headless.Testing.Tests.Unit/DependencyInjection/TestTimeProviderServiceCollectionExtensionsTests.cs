// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.DependencyInjection;
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
    public void should_return_same_instance_as_resolved_from_di()
    {
        var services = new ServiceCollection();

        var returned = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<TimeProvider>();

        resolved.Should().BeSameAs(returned);
    }

    [Fact]
    public void should_reflect_time_advance_through_di()
    {
        var services = new ServiceCollection();

        var fakeTimeProvider = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<TimeProvider>();

        var before = resolved.GetUtcNow();
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(10));
        var after = resolved.GetUtcNow();

        (after - before).Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void should_replace_a_prior_system_time_provider_registration()
    {
        var services = new ServiceCollection();

        // Provider packages register TimeProvider.System defensively via TryAddSingleton.
        services.AddSingleton(TimeProvider.System);

        services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<TimeProvider>().Should().BeOfType<FakeTimeProvider>();
    }

    [Fact]
    public void should_work_without_prior_registrations()
    {
        var services = new ServiceCollection();

        var fakeTimeProvider = services.AddTestTimeProvider();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<TimeProvider>().Should().BeSameAs(fakeTimeProvider);
    }
}
