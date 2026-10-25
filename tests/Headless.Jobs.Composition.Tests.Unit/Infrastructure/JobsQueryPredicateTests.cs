// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
            OwnerId = ownerId,
            LockedUntil = lockedUntil,
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
            OwnerId = ownerId,
            LockedUntil = lockedUntil,
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
        // The core behavior change from WhereCanAcquire: an unowned, never-locked idle row is NOT reclaimed.
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

    #region WhereCanAcquire lease-expiry arm

    private static readonly DateTime _Now = new(2026, 06, 16, 12, 00, 00, DateTimeKind.Utc);

    [Fact]
    public void where_can_acquire_selects_a_row_whose_lease_has_expired()
    {
        // Expired lease (LockedUntil in the past) owned by another node → re-claimable by the self-heal arm.
        var expired = _TimeJob(JobStatus.Idle, ownerId: "node-b@2", lockedUntil: _Now.AddMinutes(-1));

        var selected = new[] { expired }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().ContainSingle().Which.Should().Be(expired);
    }

    [Fact]
    public void where_can_acquire_does_not_select_a_future_lease_owned_by_a_different_owner()
    {
        // Live lease (LockedUntil in the future) held by another node → NOT claimable (duplicate-suppression floor).
        var liveOther = _TimeJob(JobStatus.Queued, ownerId: "node-b@2", lockedUntil: _Now.AddMinutes(5));

        var selected = new[] { liveOther }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().BeEmpty();
    }

    [Fact]
    public void where_can_acquire_selects_a_never_leased_row()
    {
        // Unowned, never leased (LockedUntil == null) → claimable (unowned arm intact).
        var neverLeased = _TimeJob(JobStatus.Idle, ownerId: null, lockedUntil: null);

        var selected = new[] { neverLeased }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().ContainSingle().Which.Should().Be(neverLeased);
    }

    [Fact]
    public void where_can_acquire_selects_a_future_lease_owned_by_me()
    {
        // Live lease held by ME → re-claimable (self-owned crash-recovery arm intact, regardless of deadline).
        var liveMine = _TimeJob(JobStatus.Queued, ownerId: _Owner, lockedUntil: _Now.AddMinutes(5));

        var selected = new[] { liveMine }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().ContainSingle().Which.Should().Be(liveMine);
    }

    [Fact]
    public void where_can_acquire_flips_selection_as_the_client_clock_advances_past_the_lease()
    {
        // Proves the comparison is against the supplied (client) clock: the same row is excluded while the lease is
        // live and selected once `now` advances past LockedUntil.
        var leaseDeadline = _Now.AddMinutes(2);
        var leased = _TimeJob(JobStatus.Queued, ownerId: "node-b@2", lockedUntil: leaseDeadline);

        var beforeExpiry = new[] { leased }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();
        var afterExpiry = new[] { leased }.AsQueryable().WhereCanAcquire(_Owner, leaseDeadline.AddSeconds(1)).ToArray();

        beforeExpiry.Should().BeEmpty();
        afterExpiry.Should().ContainSingle().Which.Should().Be(leased);
    }

    [Fact]
    public void where_can_acquire_for_occurrences_applies_the_same_lease_expiry_arm()
    {
        var expired = _Occurrence(JobStatus.Idle, ownerId: "node-b@2", lockedUntil: _Now.AddMinutes(-1));
        var liveOther = _Occurrence(JobStatus.Queued, ownerId: "node-b@2", lockedUntil: _Now.AddMinutes(5));
        var neverLeased = _Occurrence(JobStatus.Idle, ownerId: null, lockedUntil: null);

        var selected = new[] { expired, liveOther, neverLeased }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().BeEquivalentTo([expired, neverLeased]);
    }

    #endregion

    #region WhereCanAcquire ignores OnNodeDeath

    // An Idle/Queued row never started, so a lapsed lease makes it reclaimable whatever its policy: the policy only
    // decides what happens to a started (InProgress) run.

    [Theory]
    [InlineData(NodeDeathPolicy.Retry)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public void where_can_acquire_re_claims_an_expired_lease_under_every_node_death_policy(NodeDeathPolicy policy)
    {
        var idle = _TimeJob(JobStatus.Idle, "node-b@2", _Now.AddMinutes(-1), policy);
        var queued = _TimeJob(JobStatus.Queued, "node-b@2", _Now.AddMinutes(-1), policy);

        var selected = new[] { idle, queued }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().BeEquivalentTo([idle, queued]);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public void where_can_acquire_never_re_claims_a_live_foreign_lease_or_a_started_row(NodeDeathPolicy policy)
    {
        var liveForeign = _TimeJob(JobStatus.Queued, "node-b@2", _Now.AddMinutes(5), policy);
        var started = _TimeJob(JobStatus.InProgress, "node-b@2", _Now.AddMinutes(-1), policy);

        var selected = new[] { liveForeign, started }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public void where_can_acquire_re_claims_an_expired_occurrence_under_every_node_death_policy(NodeDeathPolicy policy)
    {
        var expired = _Occurrence(JobStatus.Queued, "node-b@2", _Now.AddMinutes(-1), policy);
        var started = _Occurrence(JobStatus.InProgress, "node-b@2", _Now.AddMinutes(-1), policy);

        var selected = new[] { expired, started }.AsQueryable().WhereCanAcquire(_Owner, _Now).ToArray();

        selected.Should().ContainSingle().Which.Should().Be(expired);
    }

    #endregion
}
