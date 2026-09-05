// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerTransactionalKeyedTests(SqlServerJobsCoordinationFixture fixture)
    : JobsTransactionalKeyedConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task keyed_and_ordinary_writes_share_the_outer_commit_or_rollback(bool commit) =>
        base.keyed_and_ordinary_writes_share_the_outer_commit_or_rollback(commit);

    [Fact]
    public override Task disposing_uncommitted_scope_discards_business_and_keyed_rows() =>
        base.disposing_uncommitted_scope_discards_business_and_keyed_rows();

    [Fact]
    public override Task failure_before_keyed_write_rolls_back_application_state() =>
        base.failure_before_keyed_write_rolls_back_application_state();

    [Fact]
    public override Task failed_savepoint_restoration_requires_outer_rollback() =>
        base.failed_savepoint_restoration_requires_outer_rollback();

    [Fact]
    public override Task replacement_savepoint_restores_superseded_generation_on_insert_failure() =>
        base.replacement_savepoint_restores_superseded_generation_on_insert_failure();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task commit_boundary_failure_is_not_replayed_and_rows_match_durable_outcome(bool afterCommit) =>
        base.commit_boundary_failure_is_not_replayed_and_rows_match_durable_outcome(afterCommit);

    [Fact]
    public override Task ef_execution_strategy_retries_known_rollback_with_fresh_units_of_work() =>
        base.ef_execution_strategy_retries_known_rollback_with_fresh_units_of_work();

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public override Task preflight_rejects_owned_or_different_configured_connections_without_touching_caller(
        bool onConfiguring,
        bool owned
    ) => base.preflight_rejects_owned_or_different_configured_connections_without_touching_caller(onConfiguring, owned);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task same_database_on_configuring_override_borrows_exact_caller_handles(bool keyed) =>
        base.same_database_on_configuring_override_borrows_exact_caller_handles(keyed);

    [Fact]
    public override Task post_commit_restart_failure_keeps_deadline_recoverable_by_polling() =>
        base.post_commit_restart_failure_keeps_deadline_recoverable_by_polling();

    [Fact]
    public override Task keyed_due_eligibility_and_claim_lease_use_store_time_under_node_skew() =>
        base.keyed_due_eligibility_and_claim_lease_use_store_time_under_node_skew();
}
