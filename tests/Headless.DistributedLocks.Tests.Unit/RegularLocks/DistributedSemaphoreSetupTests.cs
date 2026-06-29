// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedSemaphoreProvider>().Should().NotBeNull();
        provider
            .GetRequiredService<IDistributedSemaphoreProvider>()
            .Should()
            .BeSameAs(provider.GetRequiredService<IDistributedSemaphoreProvider>());
    }

    [Fact]
    public void should_register_semaphore_provider_once_from_builder()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

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
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(configuration);
            setup.UseInMemory();
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedSemaphoreProvider>().Should().NotBeNull();

        var options = provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value;
        options.KeyPrefix.Should().Be("semaphore-test:");
        options.MaxResourceNameLength.Should().Be(512);
    }

    [Fact]
    public void should_register_provider_via_service_provider_overload()
    {
        // given — Action<TOptions, IServiceProvider> overload resolves IServiceProvider at configure time
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMyMarker>(new MyMarkerImpl());

        // when
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(
                (opts, sp) =>
                {
                    // verify IServiceProvider is functional (resolves a registered dependency)
                    sp.GetRequiredService<IMyMarker>().Should().NotBeNull();
                    opts.KeyPrefix = "sp-overload:";
                }
            );
            setup.UseInMemory();
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedSemaphoreProvider>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value.KeyPrefix.Should().Be("sp-overload:");
    }

    [Fact]
    public void should_register_provider_as_can_receive_lock_released_seam()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider();

        // then — the provider is registered under the ICanReceiveLockReleased seam so the
        // LockReleasedConsumer wakes semaphore waiters, and it is the same singleton instance.
        var consumers = provider.GetServices<ICanReceiveLockReleased>().ToList();
        consumers.Should().ContainSingle(c => c is DistributedSemaphoreProvider);
        consumers
            .OfType<DistributedSemaphoreProvider>()
            .Single()
            .Should()
            .BeSameAs(provider.GetRequiredService<DistributedSemaphoreProvider>());
    }

    [Fact]
    public void should_fail_validation_on_start_when_options_are_invalid()
    {
        // given — set PollingCadenceFraction to an out-of-range value (validator: 0.1..0.5)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(opts =>
            {
                opts.PollingCadenceFraction = 0.99; // > 0.5 — invalid per DistributedLockOptionsValidator
            });
            setup.UseInMemory();
        });
        using var provider = services.BuildServiceProvider();

        // when — materialising options triggers the FluentValidation IValidateOptions pipeline
        var act = () => provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    private interface IMyMarker;

    private sealed class MyMarkerImpl : IMyMarker;
}
