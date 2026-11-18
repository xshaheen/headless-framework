// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryCacheHeadlessPrefixTests : TestBase
{
    [Fact]
    public void should_put_the_host_prefix_once_in_front_of_the_default_and_every_named_cache()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.ConfigureHeadlessPrefix("wt1");
        services.AddHeadlessCaching(setup =>
        {
            setup.UseInMemory(options => options.KeyPrefix = "app:");
            setup.AddNamed("orders", instance => instance.UseInMemory(options => options.KeyPrefix = "orders:"));
            setup.AddNamed("catalog", instance => instance.UseInMemory(static _ => { }));
        });

        // when
        using var provider = services.BuildServiceProvider();

        // then
        var monitor = provider.GetRequiredService<IOptionsMonitor<InMemoryCacheOptions>>();
        monitor.CurrentValue.KeyPrefix.Should().Be("wt1:app:");
        monitor.Get("orders").KeyPrefix.Should().Be("wt1:orders:");
        monitor.Get("catalog").KeyPrefix.Should().Be("wt1:");
    }

    [Fact]
    public void should_leave_the_key_prefix_unchanged_when_the_host_has_no_prefix()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddHeadlessCaching(setup => setup.UseInMemory(options => options.KeyPrefix = "app:"));

        // when
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<InMemoryCacheOptions>>().Value.KeyPrefix.Should().Be("app:");
    }
}
