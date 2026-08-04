// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;

namespace Tests;

public sealed class TerminateExecutionExceptionTests
{
    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.DueDone)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Skipped)]
    public void accepts_terminal_statuses(JobStatus status)
    {
        var act = () => new TerminateExecutionException(status, "reason");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(JobStatus.Idle)]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.InProgress)]
    public void rejects_non_terminal_statuses_at_construction(JobStatus status)
    {
        // The handler stamps this status verbatim as the row's final state; a non-terminal status leaves the
        // owner and lease intact while re-satisfying the claim predicates — the same node re-claims and re-runs
        // the job immediately in an unbounded hot loop with no retry accounting.
        var act = () => new TerminateExecutionException(status, "defer this");

        act.Should().Throw<ArgumentOutOfRangeException>();

        var actWithInner = () => new TerminateExecutionException(status, "defer this", new InvalidOperationException());

        actWithInner.Should().Throw<ArgumentOutOfRangeException>();
    }
}
