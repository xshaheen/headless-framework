// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Verifies <c>AddHeadlessBlobs</c> default + named registration and per-instance isolation for the Redis
/// provider. Registration-shape-only — no live Redis required; <see cref="IConnectionMultiplexer"/> is
/// substituted so the factory resolves without network I/O.
/// </summary>
public sealed class RedisBlobsRegistrationTests
{
    private static IConnectionMultiplexer _CreateMockMultiplexer()
    {
        return Substitute.For<IConnectionMultiplexer>();
    }

    // RedisBlobStorage resolves TimeProvider (and the blobs core resolves IMimeTypeProvider); register them like the
    // other provider registration tests so the container can construct the store.
    private static ServiceCollection _BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    [Fact]
    public async Task default_store_is_injectable_and_named_stores_resolve_via_provider()
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer());
            blobs.AddNamed(
                "cache",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer())
            );
            blobs.AddNamed(
                "scratch",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer())
            );
        });
        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var defaultStorage = serviceProvider.GetService<IBlobStorage>();
        var provider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var cache = provider.GetStorage("cache");
        var scratch = provider.GetStorage("scratch");

        // then
        defaultStorage.Should().NotBeNull();
        cache.Should().NotBeNull();
        scratch.Should().NotBeNull();
        cache.Should().NotBeSameAs(scratch);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("cache").Should().BeSameAs(cache);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("scratch").Should().BeSameAs(scratch);
    }

    [Fact]
    public async Task named_only_configuration_leaves_default_unresolved()
    {
        // given
        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed(
                "cache",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer())
            );
            blobs.AddNamed(
                "scratch",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer())
            );
        });
        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var provider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var cache = provider.GetStorage("cache");
        var scratch = provider.GetStorage("scratch");

        // then — two named stores are distinct instances
        cache.Should().NotBeSameAs(scratch);

        // and — no default unkeyed IBlobStorage is registered
        serviceProvider.GetService<IBlobStorage>().Should().BeNull();
    }

    [Fact]
    public async Task container_manager_resolves_for_default_and_named_stores()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer());
            blobs.AddNamed(
                "cache",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = _CreateMockMultiplexer())
            );
        });
        await using var serviceProvider = services.BuildServiceProvider();

        // then — the container-management capability is a separately-resolved service (default + keyed), never a cast
        // from IBlobStorage.
        serviceProvider.GetService<IBlobContainerManager>().Should().NotBeNull();
        serviceProvider.GetRequiredKeyedService<IBlobContainerManager>("cache").Should().NotBeNull();
    }

    [Fact]
    public async Task named_stores_bind_their_own_connection_multiplexer()
    {
        // given — distinct multiplexers per named store
        await using var cacheMultiplexer = _CreateMockMultiplexer();
        await using var scratchMultiplexer = _CreateMockMultiplexer();

        var services = _BuildBaseServices();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed(
                "cache",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = cacheMultiplexer)
            );
            blobs.AddNamed(
                "scratch",
                instance => instance.UseRedis(options => options.ConnectionMultiplexer = scratchMultiplexer)
            );
        });
        await using var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<RedisBlobStorageOptions>>();

        // then — each named options snapshot carries its own multiplexer instance (per-instance isolation)
        monitor.Get("cache").ConnectionMultiplexer.Should().BeSameAs(cacheMultiplexer);
        monitor.Get("scratch").ConnectionMultiplexer.Should().BeSameAs(scratchMultiplexer);
    }
}
