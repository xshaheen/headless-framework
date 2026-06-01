// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class DistributedSemaphoreSetupTests : TestBase
{
    [Fact]
    public void should_register_semaphore_provider_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedSemaphore<FakeDistributedSemaphoreStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedSemaphoreProvider>().Should().NotBeNull();
        provider
            .GetRequiredService<IDistributedSemaphoreProvider>()
            .Should()
            .BeSameAs(provider.GetRequiredService<IDistributedSemaphoreProvider>());
    }

    [Fact]
    public void should_be_idempotent_for_repeated_semaphore_setup_calls()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedSemaphore<FakeDistributedSemaphoreStorage>(_ => { });
        services.AddDistributedSemaphore<FakeDistributedSemaphoreStorage>(_ => { });

        // then
        services.Count(x => x.ServiceType == typeof(IDistributedSemaphoreProvider)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(DistributedSemaphoreProvider)).Should().Be(1);
    }

    [Fact]
    public void should_register_provider_via_configuration_overload()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["KeyPrefix"] = "semaphore-test:",
                    ["MaxResourceNameLength"] = "512",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedSemaphore<FakeDistributedSemaphoreStorage>(configuration);
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedSemaphoreProvider>().Should().NotBeNull();

        var options = provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value;
        options.KeyPrefix.Should().Be("semaphore-test:");
        options.MaxResourceNameLength.Should().Be(512);
    }
}
