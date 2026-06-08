// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public abstract class CommitCoordinationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ICommitCoordinationFixture
{
    public virtual async Task should_run_commit_work_once_after_commit_signal()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);
        var calls = 0;

        scope.Coordinator.OnCommit((context, _) =>
        {
            context.Outcome.Should().Be(CommitOutcome.Committed);
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.Committed, AbortToken);
        await scope.SignalAsync(CommitOutcome.Committed, AbortToken);

        calls.Should().Be(1);
    }

    public virtual async Task should_discard_commit_work_after_rollback_signal()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);
        var calls = 0;

        scope.Coordinator.OnCommit((_, _) =>
        {
            calls++;

            return ValueTask.CompletedTask;
        });

        await scope.SignalAsync(CommitOutcome.RolledBack, AbortToken);

        calls.Should().Be(0);
    }

    public virtual async Task should_reject_enlistment_after_terminal_signal()
    {
        await using var scope = await fixture.BeginScopeAsync(AbortToken);

        await scope.SignalAsync(CommitOutcome.Committed, AbortToken);

        scope
            .Coordinator.Invoking(x => x.OnCommit((_, _) => ValueTask.CompletedTask))
            .Should()
            .Throw<InvalidOperationException>();
    }
}
