// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Redis;
using Headless.Serializer;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public sealed class RedisCacheConformanceTests(RedisCacheFixture fixture) : CacheConformanceTestsBase
{
    protected override ICache CreateCache(string keyPrefix)
    {
        var options = new RedisCacheOptions
        {
            ConnectionMultiplexer = fixture.ConnectionMultiplexer,
            KeyPrefix = $"{keyPrefix}:",
        };

        var logger = LoggerFactory.CreateLogger<RedisCache>();
        return new RedisCache(new SystemJsonSerializer(), TimeProvider.System, options, fixture.ScriptsLoader, logger);
    }

    protected override async ValueTask ResetAsync()
    {
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    protected override async ValueTask AdvancePastExpirationAsync(TimeSpan expiration)
    {
        await TimeProvider.System.Delay(expiration + TimeSpan.FromMilliseconds(250), AbortToken);
    }

    [Fact]
    public override Task should_round_trip_object_and_string_values() => base.should_round_trip_object_and_string_values();

    [Fact]
    public override Task should_round_trip_null_and_null_sentinel_string() =>
        base.should_round_trip_null_and_null_sentinel_string();

    [Fact]
    public override Task should_round_trip_empty_string_value() => base.should_round_trip_empty_string_value();

    [Fact]
    public override Task should_expire_values_after_duration() => base.should_expire_values_after_duration();

    [Fact]
    public override Task should_get_all_values_including_null_members() =>
        base.should_get_all_values_including_null_members();

    [Fact]
    public override Task should_increment_and_read_back_number() => base.should_increment_and_read_back_number();

    [Fact]
    public override Task should_compare_and_swap_on_matching_values_only() =>
        base.should_compare_and_swap_on_matching_values_only();

    [Fact]
    public override Task should_insert_only_when_missing_and_replace_only_when_present() =>
        base.should_insert_only_when_missing_and_replace_only_when_present();
}
