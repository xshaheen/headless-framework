// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheL1FillOrderingTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_not_fill_l1_with_a_value_read_before_a_peer_invalidation()
    {
        // given - the read has fetched the old value from L2 and is parked before it fills L1
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(_timeProvider);
        await using var cache = _CreateCache(l1, l2);
        var key = Faker.Random.AlphaNumeric(10);
        await l2.UpsertAsync(key, 1, TimeSpan.FromMinutes(5), AbortToken);
        l2.ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = cache.GetAsync<int>(key, AbortToken).AsTask();
        await l2.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when - a peer's write invalidates the key, then the parked read finishes
        await cache.HandleInvalidationAsync(
            new CacheInvalidationMessage { InstanceId = "peer", Key = key },
            AbortToken
        );
        l2.ReadGate.SetResult();
        var result = await read.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then - the read still answers, but L1 does not keep the value the invalidation superseded
        result.Value.Should().Be(1);
        (await l1.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_fill_l1_when_nothing_changed_the_key_during_the_read()
    {
        // given
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(_timeProvider);
        await using var cache = _CreateCache(l1, l2);
        var key = Faker.Random.AlphaNumeric(10);
        await l2.UpsertAsync(key, 1, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var result = await cache.GetAsync<int>(key, AbortToken);

        // then
        result.Value.Should().Be(1);
        (await l1.GetAsync<int>(key, AbortToken)).Value.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_invalidate_an_in_flight_first_l1_fill_only_when_the_tag_matches(bool matches)
    {
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2 = new GatedRemoteCache(_timeProvider);
        await using var cache = _CreateCache(l1, l2);
        const string key = "entry";
        const string tag = "entry-tag";
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = [tag] };
        await l2.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);
        l2.ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = cache.GetAsync<string>(key, AbortToken).AsTask();
        await l2.ReadStarted.Task.Bounded();

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        try
        {
            await cache.HandleInvalidationAsync(
                new CacheInvalidationMessage { InstanceId = "peer", Tag = matches ? tag : "unrelated" },
                AbortToken
            );
        }
        finally
        {
            l2.ReadGate.SetResult();
        }

        (await read).Value.Should().Be("value");
        (await l1.GetAsync<string>(key, AbortToken)).HasValue.Should().Be(!matches);
    }

    [Fact]
    public async Task should_drop_the_local_counter_after_an_increment_instead_of_writing_its_result()
    {
        // given - concurrent increments finish in either order, so a written result could move L1 backwards
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions { CloneValues = true });
        using var l2 = new GatedRemoteCache(_timeProvider);
        await using var cache = _CreateCache(l1, l2);
        var key = Faker.Random.AlphaNumeric(10);
        await l1.UpsertAsync(key, 4L, TimeSpan.FromMinutes(5), AbortToken);

        // when
        var total = await cache.IncrementAsync(key, 1L, TimeSpan.FromMinutes(5), AbortToken);

        // then - L1 holds nothing, and the next read takes the total from L2
        total.Should().Be(1L);
        (await l1.GetAsync<long>(key, AbortToken)).HasValue.Should().BeFalse();
        (await cache.GetAsync<long>(key, AbortToken)).Value.Should().Be(1L);
    }

    private HybridCache _CreateCache(IInMemoryCache l1, IRemoteCache l2)
    {
        var publisher = Substitute.For<IHybridCacheBackplane>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        return new HybridCache(
            l1,
            l2,
            publisher,
            new HybridCacheOptions { InstanceId = "this-node" },
            timeProvider: _timeProvider
        );
    }
}
