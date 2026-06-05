// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.ReaderWriterLocks;

public sealed class DistributedReaderWriterLockSetupTests : TestBase
{
    [Fact]
    public void should_register_reader_writer_lock_provider_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedReaderWriterLockProvider>().Should().NotBeNull();
        provider
            .GetRequiredService<IDistributedReaderWriterLockProvider>()
            .Should()
            .BeSameAs(provider.GetRequiredService<IDistributedReaderWriterLockProvider>());
    }

    [Fact]
    public void should_be_idempotent_for_repeated_reader_writer_setup_calls()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(_ => { });
        services.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(_ => { });

        // then
        services.Count(x => x.ServiceType == typeof(IDistributedReaderWriterLockProvider)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(DistributedReaderWriterLockProvider)).Should().Be(1);
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
        services.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(configuration);
        using var provider = services.BuildServiceProvider();

        // then
        var resolved = provider.GetRequiredService<IDistributedReaderWriterLockProvider>();
        resolved.Should().BeSameAs(provider.GetRequiredService<IDistributedReaderWriterLockProvider>());

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
        services.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(
            (opts, sp) =>
            {
                var marker = sp.GetRequiredService<MarkerService>();
                opts.KeyPrefix = marker.Prefix;
            }
        );
        using var provider = services.BuildServiceProvider();

        // then
        var resolved = provider.GetRequiredService<IDistributedReaderWriterLockProvider>();
        resolved.Should().NotBeNull();

        var options = provider.GetRequiredService<IOptions<DistributedLockOptions>>().Value;
        options.KeyPrefix.Should().Be("custom-prefix:");
    }

    private sealed class MarkerService
    {
        public required string Prefix { get; init; }
    }
}
