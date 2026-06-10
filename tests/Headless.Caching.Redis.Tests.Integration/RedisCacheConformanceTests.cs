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

    protected override async ValueTask AdvanceAsync(TimeSpan duration)
    {
        await TimeProvider.System.Delay(duration, AbortToken);
    }

    [Fact]
    public override Task should_round_trip_object_and_string_values() =>
        base.should_round_trip_object_and_string_values();

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

    [Fact]
    public override Task should_serve_stale_when_failsafe_factory_throws_within_window() =>
        base.should_serve_stale_when_failsafe_factory_throws_within_window();

    [Fact]
    public override Task should_propagate_factory_exception_after_failsafe_physical_window() =>
        base.should_propagate_factory_exception_after_failsafe_physical_window();

    [Fact]
    public override Task should_propagate_factory_exception_when_failsafe_cache_is_cold() =>
        base.should_propagate_factory_exception_when_failsafe_cache_is_cold();

    [Fact]
    public override Task should_throttle_failsafe_factory_retries() => base.should_throttle_failsafe_factory_retries();

    [Fact]
    public override Task should_not_serve_stale_when_failsafe_disabled_by_default() =>
        base.should_not_serve_stale_when_failsafe_disabled_by_default();

    [Fact]
    public override Task should_not_serve_stale_when_caller_cancels() =>
        base.should_not_serve_stale_when_caller_cancels();

    [Fact]
    public override Task should_return_stale_on_soft_timeout_and_refresh_in_background() =>
        base.should_return_stale_on_soft_timeout_and_refresh_in_background();

    [Fact]
    public override Task should_throw_cache_factory_timeout_when_hard_timeout_fires_without_fallback() =>
        base.should_throw_cache_factory_timeout_when_hard_timeout_fires_without_fallback();

    [Fact]
    public override Task should_serve_stale_when_hard_timeout_fires_with_fallback() =>
        base.should_serve_stale_when_hard_timeout_fires_with_fallback();

    [Fact]
    public override Task should_return_stale_to_waiter_when_soft_timeout_elapses_acquiring_lock() =>
        base.should_return_stale_to_waiter_when_soft_timeout_elapses_acquiring_lock();

    [Fact]
    public override Task should_eager_refresh_before_expiration() => base.should_eager_refresh_before_expiration();

    [Fact]
    public override Task should_not_stampede_eager_refresh_across_concurrent_readers() =>
        base.should_not_stampede_eager_refresh_across_concurrent_readers();

    [Fact]
    public override Task should_keep_sliding_entry_alive_when_read_within_idle_window() =>
        base.should_keep_sliding_entry_alive_when_read_within_idle_window();

    [Fact]
    public override Task should_expire_sliding_entry_at_absolute_duration_cap() =>
        base.should_expire_sliding_entry_at_absolute_duration_cap();

    [Fact]
    public override Task should_not_rearm_sliding_entry_when_metadata_is_read() =>
        base.should_not_rearm_sliding_entry_when_metadata_is_read();

    [Fact]
    public override Task should_not_rearm_non_sliding_entry() => base.should_not_rearm_non_sliding_entry();

    [Fact]
    public override Task should_extend_entry_when_conditional_factory_reports_not_modified() =>
        base.should_extend_entry_when_conditional_factory_reports_not_modified();

    [Fact]
    public override Task should_replace_entry_when_conditional_factory_reports_modified() =>
        base.should_replace_entry_when_conditional_factory_reports_modified();
}
