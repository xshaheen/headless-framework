// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests.Chains;

/// <summary>
/// U5/KTD3 four-way gate parity. The timed-descendant claim gate is implemented four times by hand — the LINQ
/// <see cref="M:Microsoft.EntityFrameworkCore.HeadlessJobsQueryExtensions.WhereClaimableUnderParentTerminalGate``1"/>,
/// the native-SQL <c>TimedChildGateSql.Build</c>, the in-memory <c>_ParentGateAllowsClaim</c>, and (U2) the mismatch
/// predicate <c>WhereParentTerminalRunConditionMismatched</c> — each carrying a comment that "the three/four must stay
/// in lockstep" with nothing enforcing it. This replays one shared case grid (every <see cref="RunCondition"/>
/// including the two non-gated ones and <see langword="null"/>, against every terminal status plus a non-terminal
/// control) against the C#/LINQ implementations, anchored on <see cref="ChainRunConditionRules"/> — the single source
/// of truth the in-memory gate delegates to. The native-SQL implementation is replayed against the same grid on each
/// relational provider in the conformance harness, so all four agree or a test fails instead of a production claim.
/// </summary>
public sealed class GateParityMatrixTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    // Every run condition: the five parent-terminal-gated ones, plus the two that are intentionally NOT gated
    // (InProgress runs concurrently, null is unconditional).
    public static readonly RunCondition?[] AllConditions =
    [
        null,
        RunCondition.InProgress,
        RunCondition.OnSuccess,
        RunCondition.OnFailure,
        RunCondition.OnCancelled,
        RunCondition.OnFailureOrCancelled,
        RunCondition.OnAnyCompletedStatus,
    ];

    // Every terminal status the gate distinguishes, plus a non-terminal control (Queued) that must never satisfy it.
    public static readonly JobStatus[] AllParentStatuses =
    [
        JobStatus.Succeeded,
        JobStatus.DueDone,
        JobStatus.Failed,
        JobStatus.Cancelled,
        JobStatus.Skipped,
        JobStatus.Queued,
    ];

    public static TheoryData<RunCondition?, JobStatus> Cases()
    {
        var data = new TheoryData<RunCondition?, JobStatus>();
        foreach (var condition in AllConditions)
        {
            foreach (var status in AllParentStatuses)
            {
                data.Add(condition, status);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void linq_claim_gate_matches_the_shared_rules(RunCondition? condition, JobStatus parentStatus)
    {
        var (childQuery, allQuery, childId) = _Build(condition, parentStatus);

        // A gated timed child is claimable only when the parent matches; a non-gated one (InProgress/null) is always
        // claimable. This is exactly WhereClaimableUnderParentTerminalGate's escape-arm-or-match shape.
        var gated = ChainRunConditionRules.IsParentTerminalGated(condition);
        var expectedClaimable = !gated || ChainRunConditionRules.ParentTerminalMatches(condition, parentStatus);

        var claimable = childQuery.WhereClaimableUnderParentTerminalGate(allQuery).Any(x => x.Id == childId);

        claimable.Should().Be(expectedClaimable);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void linq_mismatch_predicate_matches_the_shared_rules(RunCondition? condition, JobStatus parentStatus)
    {
        // The mismatch predicate is only ever applied to gated timed candidates (the escape arms are excluded
        // upstream), so parity is asserted over exactly that domain — mirroring production usage.
        if (!ChainRunConditionRules.IsParentTerminalGated(condition))
        {
            return;
        }

        var (childQuery, allQuery, childId) = _Build(condition, parentStatus);

        var expectedMismatched =
            ChainRunConditionRules.IsTerminal(parentStatus)
            && !ChainRunConditionRules.ParentTerminalMatches(condition, parentStatus);

        var mismatched = childQuery.WhereParentTerminalRunConditionMismatched(allQuery).Any(x => x.Id == childId);

        mismatched.Should().Be(expectedMismatched);
    }

    // The claim gate and the mismatch predicate must partition a gated terminal candidate: a child whose parent has
    // settled is either claimable (parent matched) or mismatched (parent did not) — never both, never neither.
    [Theory]
    [MemberData(nameof(Cases))]
    public void claim_and_mismatch_partition_a_gated_terminal_child(RunCondition? condition, JobStatus parentStatus)
    {
        if (
            !ChainRunConditionRules.IsParentTerminalGated(condition) || !ChainRunConditionRules.IsTerminal(parentStatus)
        )
        {
            return;
        }

        var (childQuery, allQuery, childId) = _Build(condition, parentStatus);

        var claimable = childQuery.WhereClaimableUnderParentTerminalGate(allQuery).Any(x => x.Id == childId);
        var mismatched = childQuery.WhereParentTerminalRunConditionMismatched(allQuery).Any(x => x.Id == childId);

        (claimable ^ mismatched)
            .Should()
            .BeTrue("a settled gated child is exactly one of claimable or mismatched, never both or neither");
    }

    private static (IQueryable<FakeTimeJob> Child, IQueryable<FakeTimeJob> All, Guid ChildId) _Build(
        RunCondition? condition,
        JobStatus parentStatus
    )
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var parent = new FakeTimeJob
        {
            Id = parentId,
            Function = "parent",
            Status = parentStatus,
        };
        var child = new FakeTimeJob
        {
            Id = childId,
            Function = "child",
            Status = JobStatus.Idle,
            ParentId = parentId,
            ExecutionTime = new DateTime(2026, 07, 24, 12, 00, 00, DateTimeKind.Utc),
            RunCondition = condition,
        };

        var all = new List<FakeTimeJob> { parent, child }.AsQueryable();
        var childOnly = new List<FakeTimeJob> { child }.AsQueryable();
        return (childOnly, all, childId);
    }
}
