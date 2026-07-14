// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.ReaderWriterLocks;

public sealed class DistributedReadWriteLockSetupTests : TestBase
{
    [Fact]
    public void should_register_reader_writer_lock_provider_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedReadWriteLock>().Should().NotBeNull();
        provider
            .GetRequiredService<IDistributedReadWriteLock>()
            .Should()
            .BeSameAs(provider.GetRequiredService<IDistributedReadWriteLock>());
    }

    [Fact]
    public void should_register_reader_writer_provider_once_from_builder()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        // then
        services.Count(x => x.ServiceType == typeof(IDistributedReadWriteLock)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(DistributedReadWriteLock)).Should().Be(1);
    }

    [Fact]
    public void should_register_provider_via_IConfiguration_overload()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["KeyPrefix"] = "rw-test:",
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
        var resolved = provider.GetRequiredService<IDistributedReadWriteLock>();
        resolved.Should().BeSameAs(provider.GetRequiredService<IDistributedReadWriteLock>());

        var options = provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value;
        options.KeyPrefix.Should().Be("rw-test:");
        options.MaxResourceNameLength.Should().Be(512);
    }

    [Fact]
    public void should_register_provider_via_action_iserviceprovider_overload()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new MarkerService { Prefix = "custom-prefix:" });

        // when - the Action<TOption, IServiceProvider> overload lets options pull values from DI.
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(
                (opts, sp) =>
                {
                    var marker = sp.GetRequiredService<MarkerService>();
                    opts.KeyPrefix = marker.Prefix;
                }
            );
            setup.UseInMemory();
        });
        using var provider = services.BuildServiceProvider();

        // then
        var resolved = provider.GetRequiredService<IDistributedReadWriteLock>();
        resolved.Should().NotBeNull();

        var options = provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value;
        options.KeyPrefix.Should().Be("custom-prefix:");
    }

    private sealed class MarkerService
    {
        public required string Prefix { get; init; }
    }
}
