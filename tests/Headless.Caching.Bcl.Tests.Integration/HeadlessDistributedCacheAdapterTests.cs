// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection(nameof(BclRedisFixture))]
public sealed class HeadlessDistributedCacheAdapterTests(BclRedisFixture fixture) : TestBase
{
    private const int _RedisFrameHeaderLength = 27;

    [Fact]
    public async Task should_register_bcl_distributed_cache_over_named_redis_cache()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();
        var cacheProvider = host.Services.GetRequiredService<ICacheProvider>();

        // The adapter is internal; assert by type name rather than referencing the concrete type.
        distributedCache.GetType().Name.Should().Be("HeadlessDistributedCacheAdapter");
        cacheProvider.GetCache(cacheName).Should().NotBeNull();
    }

    [Fact]
    public async Task should_round_trip_bytes_without_json_or_base64_encoding()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var key = Faker.Random.AlphaNumeric(12);
        byte[] payload = [0, 1, 2, 3, 250, 251, 252, 253, 254, 255];

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();

        await distributedCache.SetAsync(
            key,
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            AbortToken
        );

        (await distributedCache.GetAsync(key, AbortToken)).Should().Equal(payload);

        var stored = await fixture.ConnectionMultiplexer.GetDatabase().StringGetAsync(keyPrefix + key);
        stored.HasValue.Should().BeTrue();
        var storedBytes = (byte[])stored!;
        storedBytes[0].Should().Be(0xFF);
        storedBytes.AsSpan(_RedisFrameHeaderLength).ToArray().Should().Equal(payload);
    }

    // The adapter implements IBufferDistributedCache; Redis implements IBufferCache, so these drive the
    // zero-intermediate-copy buffer path (TryGetToAsync / UpsertRawAsync). The DI slot is IDistributedCache; the
    // BCL consumer upcasts to IBufferDistributedCache via pattern-match, mirrored here.
    [Fact]
    public async Task should_round_trip_bytes_through_the_buffer_distributed_cache_path()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var key = Faker.Random.AlphaNumeric(12);
        var payload = Faker.Random.Bytes(2048);

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var bufferCache = (IBufferDistributedCache)host.Services.GetRequiredService<IDistributedCache>();

        await bufferCache.SetAsync(
            key,
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            AbortToken
        );

        var destination = new ArrayBufferWriter<byte>();
        var found = await bufferCache.TryGetAsync(key, destination, AbortToken);

        found.Should().BeTrue();
        destination.WrittenSpan.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task should_return_false_and_write_nothing_on_a_buffer_distributed_cache_miss()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var bufferCache = (IBufferDistributedCache)host.Services.GetRequiredService<IDistributedCache>();

        var destination = new ArrayBufferWriter<byte>();
        var found = await bufferCache.TryGetAsync(Faker.Random.AlphaNumeric(12), destination, AbortToken);

        found.Should().BeFalse();
        destination.WrittenCount.Should().Be(0);
    }

    [Fact]
    public async Task should_round_trip_a_multi_segment_sequence_byte_identically_through_the_buffer_set()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var key = Faker.Random.AlphaNumeric(12);

        // A multi-segment ReadOnlySequence exercises the segment-spanning copy in the raw upsert fast path, where a
        // single-segment input would not.
        var expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var sequence = _MultiSegment([1, 2, 3], [4, 5, 6], [7, 8, 9]);

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var bufferCache = (IBufferDistributedCache)host.Services.GetRequiredService<IDistributedCache>();

        await bufferCache.SetAsync(
            key,
            sequence,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            AbortToken
        );

        var destination = new ArrayBufferWriter<byte>();
        var found = await bufferCache.TryGetAsync(key, destination, AbortToken);

        found.Should().BeTrue();
        destination.WrittenSpan.ToArray().Should().Equal(expected);
    }

    [Fact]
    public async Task should_support_sync_get_set_refresh_and_remove()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";
        var key = Faker.Random.AlphaNumeric(12);
        byte[] payload = [1, 3, 5, 7];

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();

        distributedCache.Set(
            key,
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                SlidingExpiration = TimeSpan.FromMinutes(1),
            }
        );
        distributedCache.Refresh(key);

        distributedCache.Get(key).Should().Equal(payload);

        distributedCache.Remove(key);

        distributedCache.Get(key).Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_setting_null_bytes()
    {
        var cacheName = $"bcl-{Faker.Random.AlphaNumeric(8)}";
        var keyPrefix = $"{cacheName}:";

        using var host = await _StartHostAsync(cacheName, keyPrefix, TimeSpan.FromHours(8));
        var distributedCache = host.Services.GetRequiredService<IDistributedCache>();

        var act = () =>
            distributedCache.SetAsync(
                "null-value",
                null!,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<IHost> _StartHostAsync(string cacheName, string keyPrefix, TimeSpan defaultAbsoluteExpiration)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.UseRedis(options => options.ConnectionMultiplexer = fixture.ConnectionMultiplexer);
            setup.UseBclCache(
                options =>
                {
                    options.CacheName = cacheName;
                    options.DefaultAbsoluteExpiration = defaultAbsoluteExpiration;
                },
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

    private static ReadOnlySequence<byte> _MultiSegment(params byte[][] segments)
    {
        Segment? first = null;
        Segment? last = null;

        foreach (var segment in segments)
        {
            last = last is null ? first = new Segment(segment) : last.Append(segment);
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }
}
