// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Tests.Infrastructure;

public sealed class JobsQueryPredicateTests
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@5";

    private static FakeTimeJob _TimeJob(
        JobStatus status,
        string? ownerId,
        DateTime? lockedUntil = null,
        NodeDeathPolicy onNodeDeath = NodeDeathPolicy.Retry
    )
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            Lease = TestLeases.Held(ownerId, lockedUntil),
            OnNodeDeath = onNodeDeath,
        };
    }

    private static CronJobOccurrenceEntity<FakeCronJob> _Occurrence(
        JobStatus status,
        string? ownerId,
        DateTime? lockedUntil = null,
        NodeDeathPolicy onNodeDeath = NodeDeathPolicy.Retry
    )
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            Lease = TestLeases.Held(ownerId, lockedUntil),
            OnNodeDeath = onNodeDeath,
        };
    }

    [Fact]
    public void where_owned_by_selects_non_terminal_rows_owned_by_the_dead_incarnation()
    {
        var idle = _TimeJob(JobStatus.Idle, _Owner, lockedUntil: DateTime.UtcNow);
        var queued = _TimeJob(JobStatus.Queued, _Owner, lockedUntil: DateTime.UtcNow);
        var inProgress = _TimeJob(JobStatus.InProgress, _Owner, lockedUntil: DateTime.UtcNow);

        var selected = new[] { idle, queued, inProgress }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEquivalentTo([idle, queued, inProgress]);
    }

    [Fact]
    public void where_owned_by_drops_the_loose_unowned_idle_arm()
    {
        // Unlike the acquire predicate, a never-leased idle row nobody holds is not reclaimed.
        var unowned = _TimeJob(JobStatus.Idle, ownerId: null, lockedUntil: null);

        var selected = new[] { unowned }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }

    [Fact]
    public void where_owned_by_does_not_touch_a_fast_restart_incarnation()
    {
        // Reclaiming node-a@5 must never select node-a@6's freshly-stamped rows.
        var fastRestart = _TimeJob(JobStatus.Queued, ownerId: "node-a@6", lockedUntil: DateTime.UtcNow);

        var selected = new[] { fastRestart }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.DueDone)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Skipped)]
    public void where_owned_by_preserves_the_terminal_state_guard(JobStatus terminalStatus)
    {
        var terminalOwned = _TimeJob(terminalStatus, _Owner, lockedUntil: DateTime.UtcNow);

        var selected = new[] { terminalOwned }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }

    [Fact]
    public void where_owned_by_for_occurrences_matches_the_same_strict_predicate()
    {
        var owned = _Occurrence(JobStatus.InProgress, _Owner, lockedUntil: DateTime.UtcNow);
        var unowned = _Occurrence(JobStatus.Idle, ownerId: null, lockedUntil: null);
        var otherIncarnation = _Occurrence(JobStatus.Queued, "node-a@6", lockedUntil: DateTime.UtcNow);
        var terminalOwned = _Occurrence(JobStatus.Succeeded, _Owner, lockedUntil: DateTime.UtcNow);

        var selected = new[] { owned, unowned, otherIncarnation, terminalOwned }
            .AsQueryable()
            .WhereOwnedBy(_Owner)
            .ToArray();

        selected.Should().ContainSingle().Which.Should().Be(owned);
    }

    [Fact]
    public void where_owned_by_skips_a_row_whose_lease_ended_under_that_owner()
    {
        // An ended lease keeps the holder that last held it, so the holder alone no longer means ownership.
        var released = _TimeJob(JobStatus.Idle, _Owner, lockedUntil: DateTime.UtcNow);
        released.Lease = released.Lease with { State = RowLeaseState.Released };

        var selected = new[] { released }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }
}
