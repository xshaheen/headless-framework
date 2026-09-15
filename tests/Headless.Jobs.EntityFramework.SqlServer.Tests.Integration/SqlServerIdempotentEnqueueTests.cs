// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerIdempotentEnqueueTests(SqlServerJobsCoordinationFixture fixture)
    : JobsIdempotentEnqueueConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Fact]
    public override Task same_key_inside_ttl_dedups_to_first_job() => base.same_key_inside_ttl_dedups_to_first_job();

    [Fact]
    public override Task expired_reservation_is_replaced_by_exactly_one_new_job() =>
        base.expired_reservation_is_replaced_by_exactly_one_new_job();

    [Fact]
    public override Task different_descriptor_or_scope_does_not_dedup() =>
        base.different_descriptor_or_scope_does_not_dedup();

    [Fact]
    public override Task job_terminal_state_and_retention_delete_do_not_release_key() =>
        base.job_terminal_state_and_retention_delete_do_not_release_key();

    [Fact]
    public override Task coordinated_rollback_discards_reservation_and_job_together() =>
        base.coordinated_rollback_discards_reservation_and_job_together();
}
