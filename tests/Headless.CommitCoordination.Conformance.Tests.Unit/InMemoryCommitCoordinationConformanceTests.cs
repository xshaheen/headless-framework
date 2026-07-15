// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Runs the provider-agnostic §8 conformance suite against the in-memory coordinator. Each scenario
/// is inherited from <see cref="CommitCoordinationConformanceTests{TFixture}" /> and surfaced as a
/// fact so future providers can add their own runner the same way.
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
    public override Task should_promote_child_commit_work_to_root_when_root_commits()
    {
        return base.should_promote_child_commit_work_to_root_when_root_commits();
    }

    [Fact]
    public override Task should_doom_root_and_discard_all_work_when_child_rolls_back()
    {
        return base.should_doom_root_and_discard_all_work_when_child_rolls_back();
    }

    [Fact]
    public override Task should_discard_promoted_child_work_when_root_rolls_back()
    {
        return base.should_discard_promoted_child_work_when_root_rolls_back();
    }

    [Fact]
    public override Task should_doom_root_when_child_disposed_without_signal()
    {
        return base.should_doom_root_when_child_disposed_without_signal();
    }

    [Fact]
    public override Task should_preserve_parent_slot_when_concurrent_child_flows_fork_and_dispose()
    {
        return base.should_preserve_parent_slot_when_concurrent_child_flows_fork_and_dispose();
    }

    [Fact]
    public override Task should_run_remaining_callbacks_and_surface_fault_when_first_commit_callback_throws()
    {
        return base.should_run_remaining_callbacks_and_surface_fault_when_first_commit_callback_throws();
    }

    [Fact]
    public override Task should_discard_work_when_scope_is_disposed_without_signal()
    {
        return base.should_discard_work_when_scope_is_disposed_without_signal();
    }
}
