// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlIdempotencyFixture>]
public sealed class PostgreSqlIdempotencyConformanceTests(PostgreSqlIdempotencyFixture fixture)
    : IdempotencyConformanceTests<PostgreSqlIdempotencyFixture>(fixture)
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
    public override Task should_purge_only_records_past_retention_and_never_touch_leases()
    {
        return base.should_purge_only_records_past_retention_and_never_touch_leases();
    }

    [Fact]
    public override Task should_refuse_a_live_attempt_whose_record_was_purged()
    {
        return base.should_refuse_a_live_attempt_whose_record_was_purged();
    }

    [Fact]
    public override Task should_purge_records_and_then_their_ended_leases_from_the_retention_service()
    {
        return base.should_purge_records_and_then_their_ended_leases_from_the_retention_service();
    }
}
