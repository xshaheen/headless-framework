// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Behavioral tests for <see cref="InMemoryCache.ExpireAsync"/> — logical expiration that preserves a fail-safe
/// reserve when (and only when) the entry was written with fail-safe (Physical &gt; Logical, non-sliding).
/// </summary>
public sealed class InMemoryCacheExpireTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache(InMemoryCacheOptions? options = null)
    {
        options ??= new InMemoryCacheOptions { CloneValues = true };
        return new InMemoryCache(_timeProvider, options);
    }

    private static CacheEntryOptions _FailSafeOptions()
    {
        return new()
        {
            Duration = TimeSpan.FromMinutes(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(10),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
        };
    }

    [Fact]
    public async Task should_preserve_reserve_when_expiring_a_failsafe_entry()
    {
        // given — a fresh fail-safe entry (Physical >> Logical, non-sliding)
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var staleValue = Faker.Random.Int(1, 100);
        var options = _FailSafeOptions();

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(staleValue), options, AbortToken);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — ExpireAsync succeeds and a plain read now misses
        expired.Should().BeTrue();
        (await cache.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();

        // and — the physical reserve survived: a failing fail-safe factory still serves the stale value
        var result = await cache.GetOrAddAsync<int>(
            key,
            _ => throw new InvalidOperationException("upstream unavailable"),
            options,
            AbortToken
        );

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(staleValue);
        result.IsStale.Should().BeTrue("the preserved reserve is served as stale");
    }

    [Fact]
    public async Task should_remove_outright_when_expiring_a_non_failsafe_entry()
    {
        // given — a plain (non-fail-safe) entry: Physical == Logical, no reserve to keep
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<int?>(Faker.Random.Int(1, 100)),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — succeeds, read misses, and the entry is physically gone
        expired.Should().BeTrue();
        (await cache.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await cache.ExistsAsync(key, AbortToken)).Should().BeFalse();
        (await cache.GetExpirationAsync(key, AbortToken)).Should().BeNull();

        // and — a subsequent failing fail-safe factory has NO reserve to fall back on: it propagates
        var act = async () =>
            await cache.GetOrAddAsync<int>(
                key,
                _ => throw new InvalidOperationException("no reserve"),
                _FailSafeOptions(),
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("no reserve");
    }

    [Fact]
    public async Task should_remove_outright_when_expiring_a_sliding_entry()
    {
        // given — a sliding entry whose absolute Duration cap exceeds the idle window (Physical > Logical),
        // but that surplus is the sliding cap, NOT a fail-safe reserve, so it must collapse to a removal.
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            SlidingExpiration = TimeSpan.FromMinutes(1),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(Faker.Random.Int(1, 100)), options, AbortToken);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — succeeds and the entry is gone (no phantom reserve manufactured)
        expired.Should().BeTrue();
        (await cache.GetAsync<int>(key, AbortToken)).HasValue.Should().BeFalse();
        (await cache.ExistsAsync(key, AbortToken)).Should().BeFalse();
        (await cache.GetExpirationAsync(key, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_return_false_when_expiring_an_absent_key()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then
        expired.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_true_when_expiring_an_existing_fresh_entry()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        await cache.GetOrAddAsync(
            key,
            _ => ValueTask.FromResult<int?>(Faker.Random.Int(1, 100)),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then
        expired.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_when_entry_is_already_physically_expired()
    {
        // given — a short fail-safe entry, advanced beyond its physical window so the reserve is gone
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(1),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromSeconds(2),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(1),
        };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<int?>(Faker.Random.Int(1, 100)), options, AbortToken);

        _timeProvider.Advance(TimeSpan.FromSeconds(5));

        // when
        var expired = await cache.ExpireAsync(key, AbortToken);

        // then — a physically-expired entry reports false
        expired.Should().BeFalse();
    }
}
