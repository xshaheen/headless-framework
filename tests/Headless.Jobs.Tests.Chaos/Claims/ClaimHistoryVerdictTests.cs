// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Claims;

/// <summary>The claim-history oracle must catch each fault it exists for.</summary>
public sealed class ClaimHistoryVerdictTests : TestBase
{
    private static readonly Guid _A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid _B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void should_pass_a_clean_race()
    {
        var verdict = _Evaluate(
            starts: [new(_A, "node-0", 5), new(_B, "node-1", 6)],
            edits: [new(_A, 2, true), new(_B, 9, false)]
        );

        verdict.Violations().Should().BeEmpty();
    }

    [Fact]
    public void should_catch_a_double_claim_when_one_row_starts_on_two_nodes()
    {
        var verdict = _Evaluate(starts: [new(_A, "node-0", 5), new(_A, "node-3", 7), new(_B, "node-1", 6)]);

        verdict.RunTwice.Should().ContainSingle().Which.Should().Contain("node-0").And.Contain("node-3");
    }

    [Fact]
    public void should_catch_a_row_that_never_ran()
    {
        var verdict = _Evaluate(starts: [new(_A, "node-0", 5)]);

        verdict.NeverRan.Should().ContainSingle().Which.Should().Be("bbbbbbbb");
    }

    [Fact]
    public void should_catch_an_edit_the_guard_accepted_after_the_row_started()
    {
        var verdict = _Evaluate(starts: [new(_A, "node-0", 5), new(_B, "node-1", 6)], edits: [new(_A, 8, true)]);

        verdict.EditAfterStart.Should().ContainSingle().Which.Should().StartWith("aaaaaaaa");
    }

    [Fact]
    public void should_not_flag_a_refused_edit_after_the_start()
    {
        var verdict = _Evaluate(starts: [new(_A, "node-0", 5), new(_B, "node-1", 6)], edits: [new(_A, 8, false)]);

        verdict.EditAfterStart.Should().BeEmpty();
    }

    [Fact]
    public void should_catch_two_holders_of_one_generation()
    {
        var verdict = _Evaluate(
            starts: [new(_A, "node-0", 5), new(_B, "node-1", 6)],
            observations:
            [
                new(_A, 3, RowLeaseState.Active, "jobs-it/node-0@1"),
                new(_A, 3, RowLeaseState.Active, "jobs-it/node-4@1"),
                new(_B, 3, RowLeaseState.Active, "jobs-it/node-1@1"),
                new(_B, 4, RowLeaseState.Active, "jobs-it/node-2@1"),
            ]
        );

        verdict.SharedGeneration.Should().ContainSingle().Which.Should().StartWith("aaaaaaaa generation 3");
    }

    [Fact]
    public void should_catch_run_state_the_run_did_not_write()
    {
        var verdict = _Evaluate(
            starts: [new(_A, "node-0", 5), new(_B, "node-1", 6)],
            finals:
            [
                new(_A, JobStatus.Idle, RowLeaseState.Idle, 0, false, 1),
                new(_B, JobStatus.Succeeded, RowLeaseState.Settled, 2, true, 3),
            ]
        );

        verdict.BadFinalState.Should().HaveCount(2);
        verdict.BadFinalState[0].Should().Contain("status Idle").And.Contain("lease Idle");
        verdict.BadFinalState[1].Should().Contain("retry count 2").And.Contain("3 attempts");
    }

    private static ClaimHistoryVerdict _Evaluate(
        List<ClaimStart> starts,
        List<ClaimEdit>? edits = null,
        List<ClaimObservation>? observations = null,
        List<ClaimFinalRow>? finals = null
    ) =>
        ClaimHistoryVerdict.Evaluate(
            [_A, _B],
            starts,
            edits ?? [],
            observations ?? [],
            finals
                ??
                [
                    new(_A, JobStatus.Succeeded, RowLeaseState.Settled, 0, true, 1),
                    new(_B, JobStatus.Succeeded, RowLeaseState.Settled, 0, true, 1),
                ]
        );
}
