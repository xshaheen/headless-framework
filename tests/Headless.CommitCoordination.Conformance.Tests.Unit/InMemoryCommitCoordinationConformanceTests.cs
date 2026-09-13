// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Runs the provider-agnostic conformance suite against the in-memory coordinator. Each scenario is
/// inherited from <see cref="CommitCoordinationConformanceTests{TFixture}" /> and surfaced as a fact so
/// future providers can add their own runner the same way.
/// </summary>
public sealed class InMemoryCommitCoordinationConformanceTests(InMemoryCommitCoordinationFixture fixture)
    : CommitCoordinationConformanceTests<InMemoryCommitCoordinationFixture>(fixture),
        IClassFixture<InMemoryCommitCoordinationFixture>
{
    [Fact]
    public override Task should_run_commit_work_once_after_commit_signal()
    {
        return base.should_run_commit_work_once_after_commit_signal();
    }

    [Fact]
    public override Task should_treat_repeated_same_outcome_signal_as_silent_no_op()
    {
        return base.should_treat_repeated_same_outcome_signal_as_silent_no_op();
    }

    [Fact]
    public override Task should_discard_commit_work_after_rollback_signal()
    {
        return base.should_discard_commit_work_after_rollback_signal();
    }

    [Fact]
    public override Task should_reject_enlistment_after_terminal_signal()
    {
        return base.should_reject_enlistment_after_terminal_signal();
    }

    [Fact]
    public override Task should_run_remaining_callbacks_in_order_and_surface_fault_after_drain()
    {
        return base.should_run_remaining_callbacks_in_order_and_surface_fault_after_drain();
    }

    [Fact]
    public override Task should_roll_back_and_discard_work_when_scope_is_disposed_without_signal()
    {
        return base.should_roll_back_and_discard_work_when_scope_is_disposed_without_signal();
    }

    [Fact]
    public override Task should_dispose_scope_local_state_on_commit()
    {
        return base.should_dispose_scope_local_state_on_commit();
    }

    [Fact]
    public override Task should_dispose_scope_local_state_on_rollback()
    {
        return base.should_dispose_scope_local_state_on_rollback();
    }

    [Fact]
    public override Task should_set_ambient_current_synchronously_when_scope_opens()
    {
        return base.should_set_ambient_current_synchronously_when_scope_opens();
    }

    [Fact]
    public override Task should_restore_outer_frame_when_nested_root_is_disposed_in_order()
    {
        return base.should_restore_outer_frame_when_nested_root_is_disposed_in_order();
    }

    [Fact]
    public override Task should_not_promote_nested_root_work_to_outer_root()
    {
        return base.should_not_promote_nested_root_work_to_outer_root();
    }

    [Fact]
    public override Task should_throw_when_outer_scope_is_disposed_while_inner_is_active()
    {
        return base.should_throw_when_outer_scope_is_disposed_while_inner_is_active();
    }

    [Fact]
    public override Task should_ignore_disposal_of_frame_its_parent_already_popped()
    {
        return base.should_ignore_disposal_of_frame_its_parent_already_popped();
    }

    [Fact]
    public override Task should_ignore_signal_after_dispose()
    {
        return base.should_ignore_signal_after_dispose();
    }

    [Fact]
    public override Task should_ignore_and_log_conflicting_second_signal()
    {
        return base.should_ignore_and_log_conflicting_second_signal();
    }

    [Fact]
    public override Task should_not_roll_back_committed_work_when_dispose_races_the_commit_drain()
    {
        return base.should_not_roll_back_committed_work_when_dispose_races_the_commit_drain();
    }

    [Fact]
    public override Task should_expose_null_relational_for_non_relational_scope()
    {
        return base.should_expose_null_relational_for_non_relational_scope();
    }

    [Fact]
    public override Task should_expose_relational_handle_for_relational_scope()
    {
        return base.should_expose_relational_handle_for_relational_scope();
    }
}
