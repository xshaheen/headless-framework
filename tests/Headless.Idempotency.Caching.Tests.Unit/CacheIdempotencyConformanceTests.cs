// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

// Every autonomous scenario applies. The enlisted and purge scenarios a cache cannot honor are allow-listed, each
// with its reason, in tests/Headless.Testing.Tests.Unit/Conformance/ConformanceCaseAllowList.cs.
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
    public override Task should_admit_exactly_once_and_complete_exactly_once_under_parallel_racers()
    {
        return base.should_admit_exactly_once_and_complete_exactly_once_under_parallel_racers();
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
