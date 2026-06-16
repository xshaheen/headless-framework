// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure;

namespace Tests.Infrastructure;

public sealed class JobsQueryPredicateTests
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@5";

    private static FakeTimeJob TimeJob(JobStatus status, string? ownerId, DateTime? lockedAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            OwnerId = ownerId,
            LockedAt = lockedAt,
        };

    private static CronJobOccurrenceEntity<FakeCronJob> Occurrence(
        JobStatus status,
        string? ownerId,
        DateTime? lockedAt = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            OwnerId = ownerId,
            LockedAt = lockedAt,
        };

    [Fact]
    public void WhereOwnedBy_selects_non_terminal_rows_owned_by_the_dead_incarnation()
    {
        var idle = TimeJob(JobStatus.Idle, _Owner, lockedAt: DateTime.UtcNow);
        var queued = TimeJob(JobStatus.Queued, _Owner, lockedAt: DateTime.UtcNow);
        var inProgress = TimeJob(JobStatus.InProgress, _Owner, lockedAt: DateTime.UtcNow);

        var selected = new[] { idle, queued, inProgress }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEquivalentTo([idle, queued, inProgress]);
    }

    [Fact]
    public void WhereOwnedBy_drops_the_loose_unowned_idle_arm()
    {
        // The core behavior change from WhereCanAcquire: an unowned, never-locked idle row is NOT reclaimed.
        var unowned = TimeJob(JobStatus.Idle, ownerId: null, lockedAt: null);

        var selected = new[] { unowned }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }

    [Fact]
    public void WhereOwnedBy_does_not_touch_a_fast_restart_incarnation()
    {
        // Reclaiming node-a@5 must never select node-a@6's freshly-stamped rows (R4 / R-2).
        var fastRestart = TimeJob(JobStatus.Queued, ownerId: "node-a@6", lockedAt: DateTime.UtcNow);

        var selected = new[] { fastRestart }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.DueDone)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Skipped)]
    public void WhereOwnedBy_preserves_the_terminal_state_guard(JobStatus terminalStatus)
    {
        var terminalOwned = TimeJob(terminalStatus, _Owner, lockedAt: DateTime.UtcNow);

        var selected = new[] { terminalOwned }.AsQueryable().WhereOwnedBy(_Owner).ToArray();

        selected.Should().BeEmpty();
    }

    [Fact]
    public void WhereOwnedBy_for_occurrences_matches_the_same_strict_predicate()
    {
        var owned = Occurrence(JobStatus.InProgress, _Owner, lockedAt: DateTime.UtcNow);
        var unowned = Occurrence(JobStatus.Idle, ownerId: null, lockedAt: null);
        var otherIncarnation = Occurrence(JobStatus.Queued, "node-a@6", lockedAt: DateTime.UtcNow);
        var terminalOwned = Occurrence(JobStatus.Succeeded, _Owner, lockedAt: DateTime.UtcNow);

        var selected = new[] { owned, unowned, otherIncarnation, terminalOwned }
            .AsQueryable()
            .WhereOwnedBy(_Owner)
            .ToArray();

        selected.Should().ContainSingle().Which.Should().Be(owned);
    }
}
