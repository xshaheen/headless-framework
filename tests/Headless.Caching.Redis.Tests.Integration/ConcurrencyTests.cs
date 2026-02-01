// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class ConcurrencyTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_handle_concurrent_increments()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        const int incrementCount = 100;

        // when
        var tasks = Enumerable
            .Range(0, incrementCount)
            .Select(_ => cache.IncrementAsync(key, 1L, TimeSpan.FromMinutes(5), AbortToken).AsTask());
        await Task.WhenAll(tasks);

        // then
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(incrementCount);
    }

    [Fact]
    public async Task should_handle_concurrent_upserts()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        const string baseKey = "concurrent-upsert:";
        const int count = 50;

        // when
        var tasks = Enumerable
            .Range(0, count)
            .Select(i =>
                cache.UpsertAsync($"{baseKey}{i}", $"value-{i}", TimeSpan.FromMinutes(5), AbortToken).AsTask()
            );
        await Task.WhenAll(tasks);

        // then
        var total = await cache.GetCountAsync(baseKey, AbortToken);
        total.Should().Be(count);
    }

    [Fact]
    public async Task should_handle_concurrent_get_and_set()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "initial", TimeSpan.FromMinutes(5), AbortToken);
        const int iterations = 50;

        // when - concurrent reads and writes
        var readTasks = Enumerable
            .Range(0, iterations)
            .Select(_ => (Task)cache.GetAsync<string>(key, AbortToken).AsTask());
        var writeTasks = Enumerable
            .Range(0, iterations)
            .Select(i => (Task)cache.UpsertAsync(key, $"value-{i}", TimeSpan.FromMinutes(5), AbortToken).AsTask());

        await Task.WhenAll(readTasks.Concat(writeTasks));

        // then - should complete without errors
        var exists = await cache.ExistsAsync(key, AbortToken);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_concurrent_try_insert()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        const int concurrency = 10;

        // when - multiple attempts to insert same key
        var tasks = Enumerable
            .Range(0, concurrency)
            .Select(i => cache.TryInsertAsync(key, $"value-{i}", TimeSpan.FromMinutes(5), AbortToken).AsTask());
        var results = await Task.WhenAll(tasks);

        // then - exactly one should succeed
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task should_handle_concurrent_set_if_higher()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = Enumerable.Range(1, 100).ToList();
        Faker.Random.Shuffle(values);

        // when - concurrent set if higher with random order
        var tasks = values.Select(v =>
            cache.SetIfHigherAsync(key, (long)v, TimeSpan.FromMinutes(5), AbortToken).AsTask()
        );
        await Task.WhenAll(tasks);

        // then - should have the highest value
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(100);
    }

    [Fact]
    public async Task should_handle_concurrent_set_if_lower()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var values = Enumerable.Range(1, 100).ToList();
        Faker.Random.Shuffle(values);

        // when - concurrent set if lower with random order
        var tasks = values.Select(v =>
            cache.SetIfLowerAsync(key, (long)v, TimeSpan.FromMinutes(5), AbortToken).AsTask()
        );
        await Task.WhenAll(tasks);

        // then - should have the lowest value
        var cached = await cache.GetAsync<long>(key, AbortToken);
        cached.Value.Should().Be(1);
    }

    [Fact]
    public async Task should_handle_concurrent_remove_all()
    {
        // given
        await FlushAsync();
        var cache = CreateCache();
        const string prefix = "concurrent-remove:";
        for (var i = 0; i < 50; i++)
        {
            await cache.UpsertAsync($"{prefix}{i}", $"value-{i}", TimeSpan.FromMinutes(5), AbortToken);
        }

        // when - concurrent removes
        var keys1 = Enumerable.Range(0, 25).Select(i => $"{prefix}{i}").ToList();
        var keys2 = Enumerable.Range(25, 25).Select(i => $"{prefix}{i}").ToList();

        var tasks = new[]
        {
            cache.RemoveAllAsync(keys1, AbortToken).AsTask(),
            cache.RemoveAllAsync(keys2, AbortToken).AsTask(),
        };
        var results = await Task.WhenAll(tasks);

        // then
        results.Sum().Should().Be(50);
        var count = await cache.GetCountAsync(prefix, AbortToken);
        count.Should().Be(0);
    }
}
