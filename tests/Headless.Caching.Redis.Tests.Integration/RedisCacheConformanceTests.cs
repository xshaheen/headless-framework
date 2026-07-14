// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Redis.Testing;
using Headless.Serializer;
using Microsoft.Extensions.Logging;

namespace Tests;

[Collection(nameof(RedisCacheFixture))]
public sealed class RedisCacheConformanceTests(RedisCacheFixture fixture) : CacheConformanceTestsBase
{
    // Sliding expiry for Redis is enforced by the live Redis TTL (re-armed on read), not the injected clock, so
    // it cannot be driven off a FakeTimeProvider — these tests must pass real time. To stay robust under
    // parallel load, the sliding-timing scenarios are overridden below with a large window read at ~mid-window
    // (margins in whole seconds), so scheduler/Redis-I/O jitter (tens of ms) never crosses an expiry boundary.
    protected override ICache CreateCache(string keyPrefix) => _CreateCache(keyPrefix, new SystemJsonSerializer());

    private ICache _CreateCache(string keyPrefix, ISerializer serializer)
    {
        var options = new RedisCacheOptions
        {
            ConnectionMultiplexer = fixture.ConnectionMultiplexer,
            KeyPrefix = $"{keyPrefix}:",
        };

        var logger = LoggerFactory.CreateLogger<RedisCache>();
        return new RedisCache(serializer, TimeProvider.System, options, fixture.ScriptsLoader, logger);
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

    // Overridden with a large (whole-second) window instead of the base's sub-second one: Redis sliding rides
    // the live Redis TTL (real time), so ~1s of slack each side keeps this deterministic under parallel load.
    [Fact]
    public override async Task should_keep_sliding_entry_alive_when_read_within_idle_window()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var sliding = TimeSpan.FromSeconds(2);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromSeconds(30), SlidingExpiration = sliding };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        var firstRead = await cache.GetAsync<string>(key, AbortToken);

        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        var secondRead = await cache.GetAsync<string>(key, AbortToken);

        // Idle a full window past the last read so the entry lapses.
        await Task.Delay(sliding + TimeSpan.FromSeconds(1), AbortToken);
        var idleRead = await cache.GetAsync<string>(key, AbortToken);

        firstRead.Value.Should().Be("value");
        secondRead.Value.Should().Be("value");
        idleRead.HasValue.Should().BeFalse();
    }

    [Fact]
    public override Task should_expire_sliding_entry_at_absolute_duration_cap() =>
        base.should_expire_sliding_entry_at_absolute_duration_cap();

    // Overridden with a whole-second window (see note above): ExistsAsync must not extend the window, so after
    // the original 2s window lapses the entry is gone despite the mid-window metadata read.
    [Fact]
    public override async Task should_not_rearm_sliding_entry_when_metadata_is_read()
    {
        await ResetAsync();
        var cache = CreateCache(Faker.Random.AlphaNumeric(8));
        var key = Faker.Random.AlphaNumeric(10);
        var sliding = TimeSpan.FromSeconds(2);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromSeconds(30), SlidingExpiration = sliding };

        await cache.GetOrAddAsync(key, _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        (await cache.ExistsAsync(key, AbortToken)).Should().BeTrue();

        // Past the original window (metadata read did not re-arm) -> expired.
        await Task.Delay(sliding, AbortToken);
        var expired = await cache.GetAsync<string>(key, AbortToken);

        expired.HasValue.Should().BeFalse();
    }

    [Fact]
    public override Task should_not_rearm_non_sliding_entry() => base.should_not_rearm_non_sliding_entry();

    [Fact]
    public override Task should_refresh_sliding_entry_without_reading_value() =>
        base.should_refresh_sliding_entry_without_reading_value();

    [Fact]
    public override Task should_not_refresh_non_sliding_entry() => base.should_not_refresh_non_sliding_entry();

    [Fact]
    public override Task should_ignore_refresh_for_missing_entry() => base.should_ignore_refresh_for_missing_entry();

    [Fact]
    public override Task should_refresh_tagged_sliding_entry() => base.should_refresh_tagged_sliding_entry();

    [Fact]
    public override Task should_not_resurrect_tag_invalidated_entry_on_refresh() =>
        base.should_not_resurrect_tag_invalidated_entry_on_refresh();

    [Fact]
    public override Task should_expire_immediately_when_upsert_duration_is_non_positive() =>
        base.should_expire_immediately_when_upsert_duration_is_non_positive();

    [Fact]
    public override Task should_extend_entry_when_conditional_factory_reports_not_modified() =>
        base.should_extend_entry_when_conditional_factory_reports_not_modified();

    [Fact]
    public override Task should_replace_entry_when_conditional_factory_reports_modified() =>
        base.should_replace_entry_when_conditional_factory_reports_modified();

    [Fact]
    public override Task should_remove_entries_by_tag() => base.should_remove_entries_by_tag();

    [Fact]
    public override Task should_remove_entry_via_any_of_its_tags() => base.should_remove_entry_via_any_of_its_tags();

    [Fact]
    public override Task should_not_remove_recreated_entry_without_tag() =>
        base.should_not_remove_recreated_entry_without_tag();

    [Fact]
    public override Task should_tag_entries_via_conditional_context_and_tagged_upsert() =>
        base.should_tag_entries_via_conditional_context_and_tagged_upsert();

    [Fact]
    public override Task should_honor_failsafe_options_in_tagged_upsert() =>
        base.should_honor_failsafe_options_in_tagged_upsert();

    [Fact]
    public override Task should_serve_tag_invalidated_entry_as_failsafe_reserve() =>
        base.should_serve_tag_invalidated_entry_as_failsafe_reserve();

    [Fact]
    public override Task should_logically_clear_with_clear_async_preserving_reserves() =>
        base.should_logically_clear_with_clear_async_preserving_reserves();

    [Fact]
    public override Task should_drop_reserves_with_flush_async() => base.should_drop_reserves_with_flush_async();

    [Fact]
    public override Task should_round_trip_raw_payload_via_buffer_path() =>
        base.should_round_trip_raw_payload_via_buffer_path();

    [Fact]
    public override Task should_round_trip_multi_segment_raw_payload_via_buffer_path() =>
        base.should_round_trip_multi_segment_raw_payload_via_buffer_path();

    [Fact]
    public override Task should_read_raw_written_payload_via_generic_path() =>
        base.should_read_raw_written_payload_via_generic_path();

    [Fact]
    public override Task should_read_generic_written_payload_via_buffer_path() =>
        base.should_read_generic_written_payload_via_buffer_path();

    [Fact]
    public override Task should_invalidate_raw_written_payload_by_tag() =>
        base.should_invalidate_raw_written_payload_by_tag();

    [Fact]
    public override Task should_return_false_and_write_nothing_on_buffer_miss() =>
        base.should_return_false_and_write_nothing_on_buffer_miss();

    [Fact]
    public override Task should_round_trip_empty_raw_payload_via_buffer_path() =>
        base.should_round_trip_empty_raw_payload_via_buffer_path();

    [Fact]
    public override Task should_expire_raw_written_payload_after_duration() =>
        base.should_expire_raw_written_payload_after_duration();

    [Fact]
    public override Task should_add_only_new_set_members_and_compare_strings_case_sensitively() =>
        base.should_add_only_new_set_members_and_compare_strings_case_sensitively();

    [Fact]
    public override Task should_evict_set_members_when_set_add_uses_zero_expiration() =>
        base.should_evict_set_members_when_set_add_uses_zero_expiration();

    [Fact]
    public override Task should_return_no_value_from_get_set_when_key_is_absent() =>
        base.should_return_no_value_from_get_set_when_key_is_absent();

    [Fact]
    public override Task should_return_no_value_from_get_set_when_page_is_past_live_members() =>
        base.should_return_no_value_from_get_set_when_page_is_past_live_members();

    [Fact]
    public override Task should_return_no_value_from_get_set_when_all_members_expired() =>
        base.should_return_no_value_from_get_set_when_all_members_expired();

    [Fact]
    public override Task should_keep_zero_total_after_decrementing_to_zero() =>
        base.should_keep_zero_total_after_decrementing_to_zero();

    [Fact]
    public override Task should_preserve_ttl_when_set_if_higher_is_a_no_op() =>
        base.should_preserve_ttl_when_set_if_higher_is_a_no_op();
}
