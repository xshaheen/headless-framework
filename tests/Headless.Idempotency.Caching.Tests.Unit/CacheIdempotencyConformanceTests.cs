// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

// Every autonomous scenario applies. The omitted ones, each with why:
// - should_serialize_parallel_enlisted_admissions_and_replay_the_winner: admits through unit.Idempotency, which the
//   cache provider refuses.
// - should_admit_a_blocked_admission_when_the_enlisted_winner_rolls_back: needs an enlisted admission a rollback undoes.
// - should_leave_no_record_when_an_enlisted_admission_rolls_back: needs an enlisted admission a rollback undoes.
// - should_refuse_a_fence_in_a_long_enlisted_unit_once_the_lease_expired: FenceAsync is enlisted-only.
// - should_make_an_admission_wait_for_an_enlisted_fence_and_then_replay: FenceAsync is enlisted-only, and a cache holds
//   no lock for an admission to wait on.
// - should_not_deadlock_admissions_racing_an_enlisted_fence_then_complete: FenceAsync is enlisted-only.
// - should_peek_without_waiting_on_an_uncommitted_write_to_the_record: needs an uncommitted enlisted write.
// - should_purge_only_records_past_retention_whose_lease_is_not_live: the cache's own expiry is the purge, so
//   PurgeAsync deletes nothing.
// - should_keep_a_live_attempt_record_past_retention_until_its_lease_expires: asserts on PurgeAsync deleting the record.
// - should_draw_a_higher_generation_after_the_record_was_purged: asserts on PurgeAsync deleting the record; the
//   provider's own tests cover a re-admission after the entry is gone.
// - should_purge_records_from_the_retention_service: asserts on the purge deleting the record.
// - should_hand_a_takeover_the_last_recovery_point_the_crashed_attempt_committed: records a point inside a unit.
// - should_refuse_a_recovery_point_from_an_attempt_that_lost_the_key_and_write_nothing: ends with an enlisted point;
//   the provider's own tests cover the autonomous refusals.
// - should_not_expose_a_recovery_point_whose_unit_rolled_back: needs an enlisted point a rollback undoes.
// - should_clear_the_recovery_point_when_the_operation_completes: records and completes inside a unit; the provider's
//   own tests cover the autonomous form.
[Collection<CacheIdempotencyFixture>]
public sealed class CacheIdempotencyConformanceTests(CacheIdempotencyFixture fixture)
    : IdempotencyConformanceTests<CacheIdempotencyFixture>(fixture)
{
    [Fact]
    public override Task should_admit_exactly_one_of_many_parallel_autonomous_admissions()
    {
        return base.should_admit_exactly_one_of_many_parallel_autonomous_admissions();
    }

    [Fact]
    public override Task should_return_conflict_for_a_different_fingerprint_and_keep_the_stored_result()
    {
        return base.should_return_conflict_for_a_different_fingerprint_and_keep_the_stored_result();
    }

    [Fact]
    public override Task should_replay_within_retention_and_admit_fresh_once_it_passes()
    {
        return base.should_replay_within_retention_and_admit_fresh_once_it_passes();
    }

    [Fact]
    public override Task should_refuse_the_expired_attempt_completion_after_a_takeover()
    {
        return base.should_refuse_the_expired_attempt_completion_after_a_takeover();
    }

    [Fact]
    public override Task should_admit_again_at_once_after_a_release()
    {
        return base.should_admit_again_at_once_after_a_release();
    }

    [Fact]
    public override Task should_keep_records_of_different_tenants_and_the_host_scope_independent()
    {
        return base.should_keep_records_of_different_tenants_and_the_host_scope_independent();
    }

    [Fact]
    public override Task should_refuse_keys_no_provider_stores_unchanged_before_any_write()
    {
        return base.should_refuse_keys_no_provider_stores_unchanged_before_any_write();
    }

    [Fact]
    public override Task should_refuse_a_second_completion_by_the_same_attempt()
    {
        return base.should_refuse_a_second_completion_by_the_same_attempt();
    }

    [Fact]
    public override Task should_renew_only_while_the_attempt_owns_the_key()
    {
        return base.should_renew_only_while_the_attempt_owns_the_key();
    }

    [Fact]
    public override Task should_peek_absent_pending_completed_and_respect_retention_and_tenant_scope()
    {
        return base.should_peek_absent_pending_completed_and_respect_retention_and_tenant_scope();
    }

    [Fact]
    public override Task should_keep_the_recovery_point_through_a_release_until_retention_resets_the_record()
    {
        return base.should_keep_the_recovery_point_through_a_release_until_retention_resets_the_record();
    }

    [Fact]
    public override Task should_refuse_an_oversized_recovery_state_before_any_write_and_store_one_at_the_limit()
    {
        return base.should_refuse_an_oversized_recovery_state_before_any_write_and_store_one_at_the_limit();
    }

    [Fact]
    public override Task should_keep_recovery_points_of_different_tenants_independent()
    {
        return base.should_keep_recovery_points_of_different_tenants_independent();
    }
}
