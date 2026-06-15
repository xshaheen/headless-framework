// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Drives <c>HeadlessOutputCacheStore</c> directly against a real Redis-backed named cache: byte fidelity, real
/// TTL expiry, real single-node tag eviction, and delegated tag-limit validation.
/// </summary>
[Collection(nameof(OutputCacheRedisFixture))]
public sealed class HeadlessOutputCacheStoreTests(OutputCacheRedisFixture fixture) : TestBase
{
    [Fact]
    public async Task set_then_get_round_trips_a_byte_identical_blob_and_misses_on_unknown_key()
    {
        using var host = await _CreateHostAsync();
        var store = host.Services.GetRequiredService<IOutputCacheStore>();
        var payload = Faker.Random.Bytes(2048);
        var key = Faker.Random.AlphaNumeric(12);

        await store.SetAsync(key, payload, tags: null, TimeSpan.FromMinutes(5), AbortToken);

        (await store.GetAsync(key, AbortToken)).Should().Equal(payload);
        (await store.GetAsync(Faker.Random.AlphaNumeric(12), AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task entry_expires_after_its_relative_ttl()
    {
        using var host = await _CreateHostAsync();
        var store = host.Services.GetRequiredService<IOutputCacheStore>();
        var key = Faker.Random.AlphaNumeric(12);

        await store.SetAsync(key, [1, 2, 3], tags: null, TimeSpan.FromSeconds(1), AbortToken);
        (await store.GetAsync(key, AbortToken)).Should().NotBeNull();

        await Task.Delay(TimeSpan.FromMilliseconds(1500), AbortToken);

        (await store.GetAsync(key, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task evict_by_tag_drops_matching_entries_and_leaves_others()
    {
        using var host = await _CreateHostAsync();
        var store = host.Services.GetRequiredService<IOutputCacheStore>();
        var tagged = Faker.Random.AlphaNumeric(12);
        var other = Faker.Random.AlphaNumeric(12);

        await store.SetAsync(tagged, [0xA], ["a", "b"], TimeSpan.FromMinutes(5), AbortToken);
        await store.SetAsync(other, [0xB], ["b"], TimeSpan.FromMinutes(5), AbortToken);

        // The tag marker invalidates entries whose CreatedAt strictly predates it; both are millisecond-precision,
        // so cross a millisecond boundary before evicting to keep the assertion deterministic on fast Redis.
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        await store.EvictByTagAsync("a", AbortToken);

        // the entry tagged "a" is gone; the entry tagged only "b" survives
        (await store.GetAsync(tagged, AbortToken)).Should().BeNull();
        (await store.GetAsync(other, AbortToken)).Should().Equal([0xB]);
    }

    [Fact]
    public async Task tag_exceeding_the_engine_envelope_limit_surfaces_the_engine_validation()
    {
        using var host = await _CreateHostAsync();
        var store = host.Services.GetRequiredService<IOutputCacheStore>();
        var oversizedTag = new string('x', 70_000); // > 65535 UTF-8 bytes

        // R7: the store delegates tag-limit validation to the engine's write-time choke point rather than
        // re-implementing it.
        var act = async () =>
            await store.SetAsync(Faker.Random.AlphaNumeric(12), [0x1], [oversizedTag], TimeSpan.FromMinutes(5), AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private async Task<IHost> _CreateHostAsync()
    {
        var cacheName = $"output-cache-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOutputCache();
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
            setup.UseOutputCache(
                options => options.CacheName = cacheName,
                instance =>
                    instance.UseRedis(options =>
                    {
                        options.ConnectionMultiplexer = fixture.ConnectionMultiplexer;
                        options.KeyPrefix = keyPrefix;
                    })
            );
        });

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        return host;
    }
}
