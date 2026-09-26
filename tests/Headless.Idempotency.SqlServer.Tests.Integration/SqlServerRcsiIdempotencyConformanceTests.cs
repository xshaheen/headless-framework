// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerRcsiIdempotencyFixture>]
public sealed class SqlServerRcsiIdempotencyConformanceTests(SqlServerRcsiIdempotencyFixture fixture)
    : IdempotencyConformanceTests<SqlServerRcsiIdempotencyFixture>(fixture)
{
    [Fact]
    public override Task should_admit_exactly_one_of_many_parallel_autonomous_admissions()
    {
        return base.should_admit_exactly_one_of_many_parallel_autonomous_admissions();
    }

    [Fact]
    public override Task should_serialize_parallel_enlisted_admissions_and_replay_the_winner()
    {
        return base.should_serialize_parallel_enlisted_admissions_and_replay_the_winner();
    }

    [Fact]
    public override Task should_admit_a_blocked_admission_when_the_enlisted_winner_rolls_back()
    {
        return base.should_admit_a_blocked_admission_when_the_enlisted_winner_rolls_back();
    }

    [Fact]
    public override Task should_return_conflict_for_a_different_fingerprint_and_keep_the_stored_result()
    {
        return base.should_return_conflict_for_a_different_fingerprint_and_keep_the_stored_result();
    }

    [Fact]
    public override Task should_leave_no_record_when_an_enlisted_admission_rolls_back()
    {
        return base.should_leave_no_record_when_an_enlisted_admission_rolls_back();
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
    public override Task should_refuse_keys_with_surrounding_whitespace_before_any_write()
    {
        return base.should_refuse_keys_with_surrounding_whitespace_before_any_write();
    }

    [Fact]
    public override Task should_make_an_admission_wait_for_an_enlisted_fence_and_then_replay()
    {
        return base.should_make_an_admission_wait_for_an_enlisted_fence_and_then_replay();
    }

    [Fact]
    public override Task should_not_deadlock_admissions_racing_an_enlisted_fence_then_complete()
    {
        return base.should_not_deadlock_admissions_racing_an_enlisted_fence_then_complete();
    }

    [Fact]
    public override Task should_peek_absent_pending_completed_and_respect_retention_and_tenant_scope()
    {
        return base.should_peek_absent_pending_completed_and_respect_retention_and_tenant_scope();
    }

    [Fact]
    public override Task should_peek_without_waiting_on_an_uncommitted_write_to_the_record()
    {
        return base.should_peek_without_waiting_on_an_uncommitted_write_to_the_record();
    }

    [Fact]
    public override Task should_purge_only_records_past_retention_whose_lease_is_not_live()
    {
        return base.should_purge_only_records_past_retention_whose_lease_is_not_live();
    }

    [Fact]
    public override Task should_keep_a_live_attempt_record_past_retention_until_its_lease_expires()
    {
        return base.should_keep_a_live_attempt_record_past_retention_until_its_lease_expires();
    }

    [Fact]
    public override Task should_purge_records_from_the_retention_service()
    {
        return base.should_purge_records_from_the_retention_service();
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
    public override Task should_refuse_a_fence_in_a_long_enlisted_unit_once_the_lease_expired()
    {
        return base.should_refuse_a_fence_in_a_long_enlisted_unit_once_the_lease_expired();
    }

    [Fact]
    public override Task should_draw_a_higher_generation_after_the_record_was_purged()
    {
        return base.should_draw_a_higher_generation_after_the_record_was_purged();
    }
}
