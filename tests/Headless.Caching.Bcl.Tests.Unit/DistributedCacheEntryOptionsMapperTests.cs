// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DistributedCacheEntryOptionsMapperTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void should_use_default_absolute_expiration_when_no_bcl_expiration_is_configured()
    {
        var mapped = DistributedCacheEntryOptionsMapper.Map(
            new DistributedCacheEntryOptions(),
            TimeSpan.FromHours(8),
            _timeProvider
        );

        mapped.Duration.Should().Be(TimeSpan.FromHours(8));
        mapped.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void should_map_absolute_expiration_relative_to_now()
    {
        var mapped = DistributedCacheEntryOptionsMapper.Map(
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) },
            TimeSpan.FromHours(8),
            _timeProvider
        );

        mapped.Duration.Should().Be(TimeSpan.FromMinutes(30));
        mapped.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void should_map_absolute_expiration_timestamp()
    {
        var mapped = DistributedCacheEntryOptionsMapper.Map(
            new DistributedCacheEntryOptions { AbsoluteExpiration = _timeProvider.GetUtcNow().AddMinutes(45) },
            TimeSpan.FromHours(8),
            _timeProvider
        );

        mapped.Duration.Should().Be(TimeSpan.FromMinutes(45));
        mapped.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void should_prefer_relative_absolute_expiration_when_both_absolute_forms_are_set()
    {
        var mapped = DistributedCacheEntryOptionsMapper.Map(
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = _timeProvider.GetUtcNow().AddHours(2),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20),
            },
            TimeSpan.FromHours(8),
            _timeProvider
        );

        mapped.Duration.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void should_keep_sliding_expiration_and_use_default_absolute_cap_when_no_absolute_expiration_is_set()
    {
        var mapped = DistributedCacheEntryOptionsMapper.Map(
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(15) },
            TimeSpan.FromHours(8),
            _timeProvider
        );

        mapped.Duration.Should().Be(TimeSpan.FromHours(8));
        mapped.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void should_keep_sliding_expiration_and_absolute_cap_when_both_are_set()
    {
        var mapped = DistributedCacheEntryOptionsMapper.Map(
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(3),
                SlidingExpiration = TimeSpan.FromMinutes(15),
            },
            TimeSpan.FromHours(8),
            _timeProvider
        );

        mapped.Duration.Should().Be(TimeSpan.FromHours(3));
        mapped.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void should_reject_expired_absolute_timestamp()
    {
        var act = () =>
            DistributedCacheEntryOptionsMapper.Map(
                new DistributedCacheEntryOptions { AbsoluteExpiration = _timeProvider.GetUtcNow().AddSeconds(-1) },
                TimeSpan.FromHours(8),
                _timeProvider
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_reject_non_positive_default_absolute_expiration()
    {
        var act = () =>
            DistributedCacheEntryOptionsMapper.Map(new DistributedCacheEntryOptions(), TimeSpan.Zero, _timeProvider);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
