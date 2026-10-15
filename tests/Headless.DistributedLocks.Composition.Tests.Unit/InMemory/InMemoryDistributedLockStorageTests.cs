// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.InMemory;

public sealed class InMemoryDistributedLockStorageTests : TestBase
{
    private readonly FakeTimeProvider _fakeTime = new();
    private readonly WallClockSteppingTimeProvider _timeProvider;
    private readonly InMemoryDistributedLockStorage _storage;

    public InMemoryDistributedLockStorageTests()
    {
        _timeProvider = new WallClockSteppingTimeProvider(_fakeTime);
        _storage = new InMemoryDistributedLockStorage(_timeProvider);
    }

    [Fact]
    public async Task should_keep_lease_until_monotonic_deadline_when_wall_clock_steps()
    {
        var key = Faker.Random.AlphaNumeric(10);
        (await _storage.InsertAsync(key, "lease-1", TimeSpan.FromSeconds(5), AbortToken)).Acquired.Should().BeTrue();

        _timeProvider.WallClockOffset = TimeSpan.FromHours(-1);
        var heldAfterBackwardStep = await _storage.GetAsync(key, AbortToken);

        _timeProvider.WallClockOffset = TimeSpan.FromHours(1);
        var heldAfterForwardStep = await _storage.GetAsync(key, AbortToken);

        _fakeTime.Advance(TimeSpan.FromSeconds(6));
        var heldAfterTtl = await _storage.GetAsync(key, AbortToken);

        heldAfterBackwardStep.Should().Be("lease-1");
        heldAfterForwardStep.Should().Be("lease-1");
        heldAfterTtl.Should().BeNull();
    }

    [Fact]
    public async Task should_report_remaining_ttl_from_monotonic_deadline_when_wall_clock_steps()
    {
        var key = $"ttl-{Faker.Random.AlphaNumeric(10)}";
        await _storage.InsertAsync(key, "lease-1", TimeSpan.FromSeconds(10), AbortToken);

        _timeProvider.WallClockOffset = TimeSpan.FromHours(1);
        _fakeTime.Advance(TimeSpan.FromSeconds(3));

        var expiration = await _storage.GetExpirationAsync(key, AbortToken);
        var listed = await _storage.GetAllWithExpirationByPrefixAsync("ttl-", AbortToken);

        expiration.Should().Be(TimeSpan.FromSeconds(7));
        listed.Should().ContainKey(key);
        listed[key].Should().Be(("lease-1", (TimeSpan?)TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public async Task should_remove_entry_when_lease_released()
    {
        var key = Faker.Random.AlphaNumeric(10);
        await _storage.InsertAsync(key, "lease-1", TimeSpan.FromSeconds(30), AbortToken);

        var released = await _storage.RemoveIfEqualAsync(key, "lease-1", AbortToken);

        released.Should().BeTrue();
        _storage.EntryCount.Should().Be(0);
    }

    [Fact]
    public async Task should_remove_entry_when_expired_lease_is_next_accessed()
    {
        var key = Faker.Random.AlphaNumeric(10);
        await _storage.InsertAsync(key, "lease-1", TimeSpan.FromSeconds(5), AbortToken);

        _fakeTime.Advance(TimeSpan.FromSeconds(6));
        var exists = await _storage.ExistsAsync(key, AbortToken);

        exists.Should().BeFalse();
        _storage.EntryCount.Should().Be(0);
    }

    [Fact]
    public async Task should_issue_greater_token_when_reacquired_after_entry_removed()
    {
        var key = Faker.Random.AlphaNumeric(10);
        var first = await _storage.InsertAsync(key, "lease-1", TimeSpan.FromSeconds(30), AbortToken);
        await _storage.RemoveIfEqualAsync(key, "lease-1", AbortToken);
        _storage.EntryCount.Should().Be(0, "the scenario needs the entry gone before the next grant");

        var second = await _storage.InsertAsync(key, "lease-2", TimeSpan.FromSeconds(30), AbortToken);

        second.Acquired.Should().BeTrue();
        second.FencingToken!.Value.Should().BeGreaterThan(first.FencingToken!.Value);
    }

    [Fact]
    public async Task should_keep_single_holder_and_increasing_tokens_when_acquire_release_runs_in_parallel()
    {
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        var key = Faker.Random.AlphaNumeric(10);
        const int workers = 16;
        const int iterations = 200;
        var holders = 0;
        var overlaps = 0;
        var regressions = 0;
        var lastToken = 0L;

        async Task contendAsync()
        {
            var leaseId = Guid.NewGuid().ToString("N");

            for (var i = 0; i < iterations; i++)
            {
                var result = await storage.InsertAsync(key, leaseId, TimeSpan.FromSeconds(30), AbortToken);

                if (!result.Acquired)
                {
                    continue;
                }

                if (Interlocked.Increment(ref holders) != 1)
                {
                    Interlocked.Increment(ref overlaps);
                }

                // Only the single holder reaches here, so this read-compare-write needs no extra sync.
                var token = result.FencingToken!.Value.Value;
                if (token <= lastToken)
                {
                    Interlocked.Increment(ref regressions);
                }

                lastToken = token;

                Interlocked.Decrement(ref holders);
                await storage.RemoveIfEqualAsync(key, leaseId, AbortToken);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(contendAsync, AbortToken)));

        overlaps.Should().Be(0);
        regressions.Should().Be(0);
        lastToken.Should().BePositive();
        storage.EntryCount.Should().Be(0);
    }
}
