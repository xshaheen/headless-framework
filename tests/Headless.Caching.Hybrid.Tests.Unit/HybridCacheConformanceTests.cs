// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheConformanceTests : CacheConformanceTestsBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<object> _disposables = [];

    protected override ICache CreateCache(string keyPrefix)
    {
        var l1Options = new InMemoryCacheOptions { CloneValues = true };
        var l1 = new InMemoryCache(_timeProvider, l1Options);
        var l2Options = new InMemoryCacheOptions { CloneValues = true };
        var l2Inner = new InMemoryCache(_timeProvider, l2Options);
        var l2 = new InMemoryRemoteCacheAdapter(l2Inner);
        var publisher = Substitute.For<IBus>();
        publisher
            .PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var cache = new HybridCache(l1, l2, publisher, new HybridCacheOptions(), timeProvider: _timeProvider);

        // The conformance base never disposes the cache it requests, so this fixture owns teardown.
        // Dispose the HybridCache before its backing L1/L2 stores (its dispose may drain into L2).
        _disposables.Add(cache);
        _disposables.Add(l1);
        _disposables.Add(l2Inner);

        return cache;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable syncDisposable:
                    syncDisposable.Dispose();
                    break;
            }
        }

        _disposables.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    protected override ValueTask AdvancePastExpirationAsync(TimeSpan expiration)
    {
        _timeProvider.Advance(expiration + TimeSpan.FromMilliseconds(50));
        return ValueTask.CompletedTask;
    }

    protected override ValueTask AdvanceAsync(TimeSpan duration)
    {
        _timeProvider.Advance(duration);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public override Task should_round_trip_object_and_string_values()
    {
        return base.should_round_trip_object_and_string_values();
    }

    [Fact]
    public override Task should_round_trip_null_and_null_sentinel_string()
    {
        return base.should_round_trip_null_and_null_sentinel_string();
    }

    [Fact]
    public override Task should_round_trip_empty_string_value()
    {
        return base.should_round_trip_empty_string_value();
    }

    [Fact]
    public override Task should_expire_values_after_duration()
    {
        return base.should_expire_values_after_duration();
    }

    [Fact]
    public override Task should_get_all_values_including_null_members()
    {
        return base.should_get_all_values_including_null_members();
    }

    [Fact]
    public override Task should_increment_and_read_back_number()
    {
        return base.should_increment_and_read_back_number();
    }

    [Fact]
    public override Task should_compare_and_swap_on_matching_values_only()
    {
        return base.should_compare_and_swap_on_matching_values_only();
    }

    [Fact]
    public override Task should_insert_only_when_missing_and_replace_only_when_present()
    {
        return base.should_insert_only_when_missing_and_replace_only_when_present();
    }

    [Fact]
    public override Task should_serve_stale_when_failsafe_factory_throws_within_window()
    {
        return base.should_serve_stale_when_failsafe_factory_throws_within_window();
    }

    [Fact]
    public override Task should_propagate_factory_exception_after_failsafe_physical_window()
    {
        return base.should_propagate_factory_exception_after_failsafe_physical_window();
    }

    [Fact]
    public override Task should_propagate_factory_exception_when_failsafe_cache_is_cold()
    {
        return base.should_propagate_factory_exception_when_failsafe_cache_is_cold();
    }

    [Fact]
    public override Task should_throttle_failsafe_factory_retries()
    {
        return base.should_throttle_failsafe_factory_retries();
    }

    [Fact]
    public override Task should_not_serve_stale_when_failsafe_disabled_by_default()
    {
        return base.should_not_serve_stale_when_failsafe_disabled_by_default();
    }

    [Fact]
    public override Task should_not_serve_stale_when_caller_cancels()
    {
        return base.should_not_serve_stale_when_caller_cancels();
    }

    [Fact]
    public override Task should_return_stale_on_soft_timeout_and_refresh_in_background()
    {
        return base.should_return_stale_on_soft_timeout_and_refresh_in_background();
    }

    [Fact]
    public override Task should_throw_cache_factory_timeout_when_hard_timeout_fires_without_fallback()
    {
        return base.should_throw_cache_factory_timeout_when_hard_timeout_fires_without_fallback();
    }

    [Fact]
    public override Task should_serve_stale_when_hard_timeout_fires_with_fallback()
    {
        return base.should_serve_stale_when_hard_timeout_fires_with_fallback();
    }

    [Fact]
    public override Task should_return_stale_to_waiter_when_soft_timeout_elapses_acquiring_lock()
    {
        return base.should_return_stale_to_waiter_when_soft_timeout_elapses_acquiring_lock();
    }

    [Fact]
    public override Task should_eager_refresh_before_expiration()
    {
        return base.should_eager_refresh_before_expiration();
    }

    [Fact]
    public override Task should_not_stampede_eager_refresh_across_concurrent_readers()
    {
        return base.should_not_stampede_eager_refresh_across_concurrent_readers();
    }

    [Fact]
    public override Task should_keep_sliding_entry_alive_when_read_within_idle_window()
    {
        return base.should_keep_sliding_entry_alive_when_read_within_idle_window();
    }

    [Fact]
    public override Task should_expire_sliding_entry_at_absolute_duration_cap()
    {
        return base.should_expire_sliding_entry_at_absolute_duration_cap();
    }

    [Fact]
    public override Task should_not_rearm_sliding_entry_when_metadata_is_read()
    {
        return base.should_not_rearm_sliding_entry_when_metadata_is_read();
    }

    [Fact]
    public override Task should_not_rearm_non_sliding_entry()
    {
        return base.should_not_rearm_non_sliding_entry();
    }

    [Fact]
    public override Task should_refresh_sliding_entry_without_reading_value()
    {
        return base.should_refresh_sliding_entry_without_reading_value();
    }

    [Fact]
    public override Task should_not_refresh_non_sliding_entry()
    {
        return base.should_not_refresh_non_sliding_entry();
    }

    [Fact]
    public override Task should_ignore_refresh_for_missing_entry()
    {
        return base.should_ignore_refresh_for_missing_entry();
    }

    [Fact]
    public override Task should_refresh_tagged_sliding_entry()
    {
        return base.should_refresh_tagged_sliding_entry();
    }

    [Fact]
    public override Task should_not_resurrect_tag_invalidated_entry_on_refresh()
    {
        return base.should_not_resurrect_tag_invalidated_entry_on_refresh();
    }

    [Fact]
    public override Task should_expire_immediately_when_upsert_duration_is_non_positive()
    {
        return base.should_expire_immediately_when_upsert_duration_is_non_positive();
    }

    [Fact]
    public override Task should_extend_entry_when_conditional_factory_reports_not_modified()
    {
        return base.should_extend_entry_when_conditional_factory_reports_not_modified();
    }

    [Fact]
    public override Task should_replace_entry_when_conditional_factory_reports_modified()
    {
        return base.should_replace_entry_when_conditional_factory_reports_modified();
    }

    [Fact]
    public override Task should_remove_entries_by_tag()
    {
        return base.should_remove_entries_by_tag();
    }

    [Fact]
    public override Task should_remove_entry_via_any_of_its_tags()
    {
        return base.should_remove_entry_via_any_of_its_tags();
    }

    [Fact]
    public override Task should_not_remove_recreated_entry_without_tag()
    {
        return base.should_not_remove_recreated_entry_without_tag();
    }

    [Fact]
    public override Task should_tag_entries_via_conditional_context_and_tagged_upsert()
    {
        return base.should_tag_entries_via_conditional_context_and_tagged_upsert();
    }

    [Fact]
    public override Task should_honor_failsafe_options_in_tagged_upsert()
    {
        return base.should_honor_failsafe_options_in_tagged_upsert();
    }

    [Fact]
    public override Task should_serve_tag_invalidated_entry_as_failsafe_reserve()
    {
        return base.should_serve_tag_invalidated_entry_as_failsafe_reserve();
    }

    [Fact]
    public override Task should_logically_clear_with_clear_async_preserving_reserves()
    {
        return base.should_logically_clear_with_clear_async_preserving_reserves();
    }

    [Fact]
    public override Task should_drop_reserves_with_flush_async()
    {
        return base.should_drop_reserves_with_flush_async();
    }

    [Fact]
    public override Task should_round_trip_raw_payload_via_buffer_path()
    {
        return base.should_round_trip_raw_payload_via_buffer_path();
    }

    [Fact]
    public override Task should_round_trip_multi_segment_raw_payload_via_buffer_path()
    {
        return base.should_round_trip_multi_segment_raw_payload_via_buffer_path();
    }

    [Fact]
    public override Task should_read_raw_written_payload_via_generic_path()
    {
        return base.should_read_raw_written_payload_via_generic_path();
    }

    [Fact]
    public override Task should_read_generic_written_payload_via_buffer_path()
    {
        return base.should_read_generic_written_payload_via_buffer_path();
    }

    [Fact]
    public override Task should_invalidate_raw_written_payload_by_tag()
    {
        return base.should_invalidate_raw_written_payload_by_tag();
    }

    [Fact]
    public override Task should_return_false_and_write_nothing_on_buffer_miss()
    {
        return base.should_return_false_and_write_nothing_on_buffer_miss();
    }

    [Fact]
    public override Task should_round_trip_empty_raw_payload_via_buffer_path()
    {
        return base.should_round_trip_empty_raw_payload_via_buffer_path();
    }

    [Fact]
    public override Task should_expire_raw_written_payload_after_duration()
    {
        return base.should_expire_raw_written_payload_after_duration();
    }

    [Fact]
    public override Task should_add_only_new_set_members_and_compare_strings_case_sensitively()
    {
        return base.should_add_only_new_set_members_and_compare_strings_case_sensitively();
    }

    [Fact]
    public override Task should_evict_set_members_when_set_add_uses_zero_expiration()
    {
        return base.should_evict_set_members_when_set_add_uses_zero_expiration();
    }

    [Fact]
    public override Task should_return_no_value_from_get_set_when_key_is_absent()
    {
        return base.should_return_no_value_from_get_set_when_key_is_absent();
    }

    [Fact]
    public override Task should_return_no_value_from_get_set_when_page_is_past_live_members()
    {
        return base.should_return_no_value_from_get_set_when_page_is_past_live_members();
    }

    [Fact]
    public override Task should_return_no_value_from_get_set_when_all_members_expired()
    {
        return base.should_return_no_value_from_get_set_when_all_members_expired();
    }

    [Fact]
    public override Task should_keep_zero_total_after_decrementing_to_zero()
    {
        return base.should_keep_zero_total_after_decrementing_to_zero();
    }

    [Fact]
    public override Task should_preserve_ttl_when_set_if_higher_is_a_no_op()
    {
        return base.should_preserve_ttl_when_set_if_higher_is_a_no_op();
    }

    [Fact]
    public override Task should_raise_set_and_hit_events_on_contract_operations()
    {
        return base.should_raise_set_and_hit_events_on_contract_operations();
    }

    [Fact]
    public override Task should_raise_remove_event_on_contract_remove()
    {
        return base.should_raise_remove_event_on_contract_remove();
    }

    [Fact]
    public override Task should_expose_events_hub_through_wrappers()
    {
        return base.should_expose_events_hub_through_wrappers();
    }
}
