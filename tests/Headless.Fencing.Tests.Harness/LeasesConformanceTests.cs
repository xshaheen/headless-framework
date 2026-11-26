// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Xunit;

namespace Tests;

/// <summary>
/// The provider-neutral contract of fenced leases, run against a real database. A provider leaf derives from it,
/// supplies its <see cref="ILeasesFixture" />, and overrides every test with <c>[Fact]</c>.
/// </summary>
/// <remarks>
/// Each test uses its own freshly named kind and resources, so tests never share a row and a sweep never sees
/// another test's leases. Expiry is produced by moving a row's instants into the database's past rather than by
/// waiting, except where the test is about the clock a statement reads.
/// </remarks>
public abstract class LeasesConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ILeasesFixture
{
    /// <summary>A duration no test outlives, so a lease granted with it stays live unless a test ages it.</summary>
    protected static readonly TimeSpan LongDuration = TimeSpan.FromMinutes(5);

    /// <summary>The shortest duration the default options accept, for tests that let the database clock run out.</summary>
    protected static readonly TimeSpan ShortDuration = TimeSpan.FromSeconds(1);

    protected TFixture Fixture { get; } = fixture;

    private void _SkipUnlessEnlistedGrant()
    {
        Assert.SkipUnless(
            Fixture.SupportsEnlistedGrant,
            "The provider refuses a grant inside a caller's unit; its own tests assert the refusal."
        );
    }

    #region Grant

    public virtual async Task should_refuse_the_older_generation_at_the_fence_after_a_takeover()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, resource));
        var second = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        first.Status.Should().Be(LeaseGrantStatus.Granted);
        second.Status.Should().Be(LeaseGrantStatus.Takeover);
        second.Lease!.Generation.Should().BeGreaterThan(first.Lease!.Generation);
        second.PreviousGeneration.Should().Be(first.Lease.Generation);

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        var stale = async () => await unit.Unit.Leases.FenceAsync(first.Lease, AbortToken);

        var thrown = await stale.Should().ThrowAsync<StaleLeaseException>();
        thrown.Which.Reason.Should().Be(LeaseFenceStatus.Stale);
        thrown.Which.Lease.Should().Be(first.Lease);

        await unit.Unit.Leases.FenceAsync(second.Lease, AbortToken);
        unit.Unit.IsRetryPrevented.Should().BeFalse("a fence only reads, so replaying it is always safe");
        await unit.CommitAsync(AbortToken);
    }

    public virtual async Task should_report_the_live_holder_and_change_nothing_when_the_lease_is_held()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        var before = await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken);
        var held = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        held.Status.Should().Be(LeaseGrantStatus.Held);
        held.IsAcquired.Should().BeFalse();
        held.Lease.Should().BeNull();
        held.HolderGeneration.Should().Be(granted.Lease!.Generation);
        held.ExpiresAt.Should().BeCloseTo(granted.ExpiresAt, TimeSpan.FromMicroseconds(1));
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken)).Should().Be(before);
    }

    public virtual async Task should_take_over_an_expired_lease_and_leave_nothing_for_the_sweep()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, resource));
        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        takeover.Status.Should().Be(LeaseGrantStatus.Takeover);
        takeover.PreviousGeneration.Should().Be(first.Lease!.Generation);

        var invoked = 0;
        var swept = await host.Leases.SweepExpiredAsync(
            kind,
            (_, _, _) =>
            {
                Interlocked.Increment(ref invoked);

                return ValueTask.CompletedTask;
            },
            limit: 10,
            AbortToken
        );

        swept.Handled.Should().BeEmpty();
        swept.Failures.Should().BeEmpty();
        invoked.Should().Be(0);

        var stored = await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken);
        stored!.State.Should().Be(StoredLeaseState.Active);
        stored.Generation.Should().Be(takeover.Lease!.Generation);
    }

    public virtual async Task should_decide_expiry_by_the_database_clock_whatever_the_application_clock()
    {
        foreach (var skew in new[] { TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(10) })
        {
            var (kind, resource) = (CreateKind(), CreateResource());
            var key = HostKey(kind, resource);
            await using var host = await Fixture.CreateHostAsync(
                timeProvider: new SkewedTimeProvider(skew),
                cancellationToken: AbortToken
            );

            var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
            var stored = await Fixture.ReadLeaseAsync(key, AbortToken);

            granted.Status.Should().Be(LeaseGrantStatus.Granted);
            (stored!.ExpiresAt - stored.GrantedAt).Should().BeCloseTo(LongDuration, TimeSpan.FromMicroseconds(1));
            (await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken))
                .Status.Should()
                .Be(LeaseGrantStatus.Held, $"a lease live on the database clock stays held under a {skew} skew");

            var renewed = await host.Leases.RenewAsync(granted.Lease!, LongDuration, AbortToken);
            renewed.Status.Should().Be(LeaseRenewalStatus.Renewed);
            renewed.ExpiresAt.Should().BeOnOrAfter(granted.ExpiresAt);

            await ExpireAsync(key);
            var aged = await Fixture.ReadLeaseAsync(key, AbortToken);
            var expired = await host.Leases.RenewAsync(granted.Lease!, LongDuration, AbortToken);

            expired.Status.Should().Be(LeaseRenewalStatus.Expired, $"the database clock decides under a {skew} skew");
            expired.ExpiresAt.Should().BeCloseTo(aged!.ExpiresAt, TimeSpan.FromMicroseconds(1));
            (await Fixture.ReadLeaseAsync(key, AbortToken))!.ExpiresAt.Should().Be(aged.ExpiresAt);
        }
    }

    public virtual async Task should_grant_exactly_one_holder_and_unique_generations_when_grants_race()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var hostA = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var winners = new List<long>();

        for (var round = 0; round < 3; round++)
        {
            var results = await Task.WhenAll(
                Enumerable
                    .Range(0, 16)
                    .Select(i =>
                        Task.Run(
                            async () =>
                                await (i % 2 == 0 ? hostA : hostB).Leases.GrantAsync(
                                    kind,
                                    resource,
                                    LongDuration,
                                    AbortToken
                                ),
                            AbortToken
                        )
                    )
            );

            var acquired = results.Where(static r => r.IsAcquired).ToList();
            acquired.Should().ContainSingle($"round {round} has one winner");
            acquired[0].Status.Should().Be(LeaseGrantStatus.Granted);
            results
                .Where(static r => !r.IsAcquired)
                .Should()
                .AllSatisfy(r =>
                {
                    r.Status.Should().Be(LeaseGrantStatus.Held);
                    r.HolderGeneration.Should().Be(acquired[0].Lease!.Generation);
                });

            winners.Add(acquired[0].Lease!.Generation);
            (await hostA.Leases.ReleaseAsync(acquired[0].Lease!, AbortToken))
                .Should()
                .Be(LeaseSettlementStatus.Released);
        }

        winners.Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
    }

    public virtual async Task should_keep_leases_of_different_tenants_and_the_host_scope_independent()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        LeaseGrantResult tenantA;
        LeaseGrantResult tenantB;

        using (host.CurrentTenant.Change("tenant-a"))
        {
            tenantA = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        }

        using (host.CurrentTenant.Change("tenant-b"))
        {
            tenantB = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        }

        var hostScope = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        tenantA.Status.Should().Be(LeaseGrantStatus.Granted);
        tenantB.Status.Should().Be(LeaseGrantStatus.Granted);
        hostScope.Status.Should().Be(LeaseGrantStatus.Granted, "the host scope is its own lease");
        tenantA.Lease!.TenantId.Should().Be("tenant-a");
        tenantB.Lease!.TenantId.Should().Be("tenant-b");
        hostScope.Lease!.TenantId.Should().BeNull();

        (await Fixture.ReadLeaseAsync(new LeaseKey("tenant-a", kind, resource), AbortToken))!
            .Generation.Should()
            .Be(tenantA.Lease.Generation);
        (await Fixture.ReadLeaseAsync(new LeaseKey("tenant-b", kind, resource), AbortToken))!
            .Generation.Should()
            .Be(tenantB.Lease.Generation);
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))!
            .Generation.Should()
            .Be(hostScope.Lease.Generation);

        // Settling one tenant's lease leaves the others held.
        (await host.Leases.SettleAsync(tenantA.Lease, AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled);

        using (host.CurrentTenant.Change("tenant-b"))
        {
            (await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken))
                .Status.Should()
                .Be(LeaseGrantStatus.Held);
        }
    }

    #endregion

    #region Renew, settle, release

    public virtual async Task should_report_stale_or_the_ended_state_when_renewing()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        (await host.Leases.RenewAsync(new FencedLease(null, kind, CreateResource(), 1), LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Stale, null), "a lease with no row is stale");

        var taken = CreateResource();
        var first = await host.Leases.GrantAsync(kind, taken, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, taken));
        var second = await host.Leases.GrantAsync(kind, taken, LongDuration, AbortToken);

        (await host.Leases.RenewAsync(first.Lease!, LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Stale, null));

        await host.Leases.SettleAsync(second.Lease!, AbortToken);
        (await host.Leases.RenewAsync(second.Lease!, LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Settled, null));

        var released = await host.Leases.GrantAsync(kind, CreateResource(), LongDuration, AbortToken);
        await host.Leases.ReleaseAsync(released.Lease!, AbortToken);
        (await host.Leases.RenewAsync(released.Lease!, LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Released, null));

        var abandonedResource = CreateResource();
        var abandoned = await host.Leases.GrantAsync(kind, abandonedResource, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, abandonedResource));
        var swept = await host.Leases.SweepExpiredAsync(kind, (_, _, _) => ValueTask.CompletedTask, 10, AbortToken);

        swept.Handled.Should().ContainSingle().Which.Resource.Should().Be(abandonedResource);
        (await host.Leases.RenewAsync(abandoned.Lease!, LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Abandoned, null));
    }

    public virtual async Task should_settle_once_and_refuse_a_stale_or_expired_settlement()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, resource));
        var current = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        (await host.Leases.SettleAsync(current.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
        var settled = await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken);
        (await host.Leases.SettleAsync(current.Lease!, AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled, "settling again at the same generation reports success");
        (await host.Leases.SettleAsync(first.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Stale);

        settled!.State.Should().Be(StoredLeaseState.Settled);
        settled.Generation.Should().Be(current.Lease!.Generation);
        settled.EndedAt.Should().NotBeNull();
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))
            .Should()
            .Be(settled, "a repeated or refused settlement writes nothing");

        var late = CreateResource();
        var lateLease = await host.Leases.GrantAsync(kind, late, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, late));

        (await host.Leases.SettleAsync(lateLease.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Expired);
        (await Fixture.ReadLeaseAsync(HostKey(kind, late), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Active, "an expired attempt no longer owns its outcome");
    }

    public virtual async Task should_release_once_and_let_the_next_grant_proceed_at_once()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        (await host.Leases.ReleaseAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Released);
        (await host.Leases.ReleaseAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Released);
        (await host.Leases.SettleAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Released);
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Released);

        var next = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        next.Status.Should().Be(LeaseGrantStatus.Granted, "a released lease is free, not taken over");
        next.Lease!.Generation.Should().BeGreaterThan(granted.Lease!.Generation);

        (await host.Leases.SettleAsync(next.Lease, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
        (await host.Leases.ReleaseAsync(next.Lease, AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled, "a settled attempt cannot be released");
    }

    #endregion

    #region Fence and enlisted calls

    public virtual async Task should_refuse_the_fence_once_the_ttl_elapses_inside_an_open_transaction()
    {
        _SkipUnlessEnlistedGrant();
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        var granted = await unit.Unit.Leases.GrantAsync(kind, resource, ShortDuration, AbortToken);
        await unit.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        // The transaction started before the grant and stays open: a clock frozen at transaction start would still
        // call the lease live.
        // real-wait: only real elapsed time on the database's own clock exposes a clock frozen at transaction start, and the open fence holds the lease row, so no other connection can age it
        await Task.Delay(ShortDuration + TimeSpan.FromMilliseconds(500), AbortToken);
        var late = async () => await unit.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        (await late.Should().ThrowAsync<StaleLeaseException>()).Which.Reason.Should().Be(LeaseFenceStatus.Expired);
        await unit.RollbackAsync();
    }

    public virtual async Task should_make_a_grant_wait_for_an_open_fence_and_then_see_its_settlement()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        await unit.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        var pending = host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken).AsTask();
        var first = await Task.WhenAny(
            pending,
            Task.Delay(LeasesFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().NotBeSameAs(pending, "the grant waits on the row the fence holds");

        (await unit.Unit.Leases.SettleAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
        await unit.CommitAsync(AbortToken);
        var next = await pending.WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);

        next.Status.Should().Be(LeaseGrantStatus.Granted, "the waiting grant sees the committed settlement");
        next.Lease!.Generation.Should().BeGreaterThan(granted.Lease!.Generation);
    }

    public virtual async Task should_issue_a_higher_generation_to_a_grant_that_waited_on_an_open_enlisted_grant()
    {
        _SkipUnlessEnlistedGrant();
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        var enlisted = await unit.Unit.Leases.GrantAsync(kind, resource, ShortDuration, AbortToken);
        var pending = host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken).AsTask();

        // Held open past the enlisted lease's expiry, so the waiter takes it over once the unit commits.
        // real-wait: the enlisted lease must expire on the database's own clock while the waiter stays blocked behind its row lock, which no other connection can age and no event reports
        await Task.Delay(ShortDuration * 2, AbortToken);
        pending.IsCompleted.Should().BeFalse("the waiter blocks on the enlisted grant's row");

        await unit.CommitAsync(AbortToken);
        var next = await pending.WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);

        enlisted.Status.Should().Be(LeaseGrantStatus.Granted);
        next.Status.Should().Be(LeaseGrantStatus.Takeover);
        next.PreviousGeneration.Should().Be(enlisted.Lease!.Generation);
        next.Lease!.Generation.Should().BeGreaterThan(enlisted.Lease.Generation);
    }

    public virtual async Task should_leave_no_row_when_an_enlisted_grant_rolls_back()
    {
        _SkipUnlessEnlistedGrant();
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        long rolledBack;

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            var granted = await unit.Unit.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
            rolledBack = granted.Lease!.Generation;
            await unit.RollbackAsync();
        }

        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken)).Should().BeNull();

        var next = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        next.Status.Should().Be(LeaseGrantStatus.Granted);
        next.Lease!.Generation.Should().BeGreaterThan(rolledBack);
    }

    public virtual async Task should_mark_only_an_observed_unit_non_retryable_and_only_for_writes()
    {
        _SkipUnlessEnlistedGrant();
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        FencedLease lease;

        await using (var owned = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            lease = (await owned.Unit.Leases.GrantAsync(kind, resource, LongDuration, AbortToken)).Lease!;
            owned.Unit.IsRetryPrevented.Should().BeFalse("replaying an owned unit re-runs the grant");
            await owned.CommitAsync(AbortToken);
        }

        await using (var observed = await Fixture.EnlistUnitAsync(host, AbortToken))
        {
            await observed.Unit.Leases.FenceAsync(lease, AbortToken);
            observed.Unit.IsRetryPrevented.Should().BeFalse("a fence only reads");

            (await observed.Unit.Leases.SettleAsync(lease, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
            observed.Unit.IsRetryPrevented.Should().BeTrue();
            await observed.CommitAsync(AbortToken);
        }

        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Settled);
    }

    public virtual async Task should_refuse_a_unit_on_another_database_before_any_statement()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        await using (var owned = await Fixture.BeginUnitOnOtherDatabaseAsync(host, AbortToken))
        {
            var act = async () => await owned.Unit.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database*");
            owned.Unit.State.Should().Be(UnitOfWorkState.Active, "a refusal leaves the unit usable");
        }

        await using (var observed = await Fixture.EnlistUnitOnOtherDatabaseAsync(host, AbortToken))
        {
            var act = async () => await observed.Unit.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database*");
            observed.Unit.IsRetryPrevented.Should().BeFalse("a refused call never marks the unit");
        }

        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken)).Should().BeNull();
    }

    #endregion

    #region Sweep and purge

    public virtual async Task should_hand_each_expired_lease_to_exactly_one_committed_handler_when_sweepers_race()
    {
        var kind = CreateKind();
        await using var hostA = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var generations = await SeedExpiredAsync(hostA, kind, 10);
        var invocations = new ConcurrentBag<string>();

        // Both sweepers wait at the gate inside their first handler, so each holds a claimed lease while the other
        // claims: they are in flight together, not one after the other.
        using var gate = new Barrier(2);

        var results = await Task.WhenAll(
            new[] { hostA, hostB }.Select(host =>
                Task.Run(
                    async () =>
                    {
                        var first = true;

                        return await host.Leases.SweepExpiredAsync(
                            kind,
                            async (lease, unit, ct) =>
                            {
                                if (first)
                                {
                                    first = false;
                                    if (!gate.SignalAndWait(LeasesFixtureExtensions.ReleaseTimeout, ct))
                                    {
                                        throw new TimeoutException("The other sweeper never claimed a lease.");
                                    }
                                }

                                invocations.Add(lease.Resource);
                                await Fixture.WriteHandoffAsync(unit, lease, ct);
                            },
                            limit: 100,
                            AbortToken
                        );
                    },
                    AbortToken
                )
            )
        );

        results.Should().AllSatisfy(r => r.Failures.Should().BeEmpty());
        results.Should().AllSatisfy(r => r.Handled.Should().NotBeEmpty("both sweepers claimed before either finished"));
        var handled = results.SelectMany(static r => r.Handled).ToList();

        // Uniqueness by resource on the leases, then set equality on the resources themselves: comparing the
        // leases with the string keys directly would never match.
        handled.Should().OnlyHaveUniqueItems(static l => l.Resource);
        handled.Select(static l => l.Resource).Should().BeEquivalentTo(generations.Keys);
        handled.Should().AllSatisfy(l => l.Generation.Should().Be(generations[l.Resource]));
        invocations.Should().OnlyHaveUniqueItems().And.HaveCount(10);

        var handoffs = await Fixture.ReadHandoffsAsync(kind, AbortToken);
        handoffs.Should().BeEquivalentTo(generations.Select(static p => new LeaseHandoff("", p.Key, p.Value)));

        foreach (var resource in generations.Keys)
        {
            (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))!
                .State.Should()
                .Be(StoredLeaseState.Abandoned);
        }
    }

    public virtual async Task should_roll_back_only_the_lease_whose_handler_threw_and_offer_it_again_later()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var generations = await SeedExpiredAsync(host, kind, 3);
        var failing = generations.Keys.First();

        var first = await host.Leases.SweepExpiredAsync(
            kind,
            async (lease, unit, ct) =>
            {
                // Written before throwing, so the handoff table proves the handler's own writes rolled back too.
                await Fixture.WriteHandoffAsync(unit, lease, ct);

                if (string.Equals(lease.Resource, failing, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("routing failed");
                }
            },
            limit: 100,
            AbortToken
        );

        first.Handled.Select(static l => l.Resource).Should().BeEquivalentTo(generations.Keys.Skip(1));
        var failure = first.Failures.Should().ContainSingle().Subject;
        failure.Lease.Resource.Should().Be(failing);
        failure.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("routing failed");
        (await Fixture.ReadLeaseAsync(HostKey(kind, failing), AbortToken))!.State.Should().Be(StoredLeaseState.Active);
        (await Fixture.ReadHandoffsAsync(kind, AbortToken))
            .Select(static h => h.Resource)
            .Should()
            .BeEquivalentTo(generations.Keys.Skip(1));

        var second = await host.Leases.SweepExpiredAsync(
            kind,
            async (lease, unit, ct) => await Fixture.WriteHandoffAsync(unit, lease, ct),
            limit: 100,
            AbortToken
        );

        second.Failures.Should().BeEmpty();
        var retried = second.Handled.Should().ContainSingle().Subject;
        retried.Resource.Should().Be(failing);
        retried.Generation.Should().Be(generations[failing], "an abandon never bumps the generation");
        (await Fixture.ReadHandoffsAsync(kind, AbortToken)).Should().HaveCount(3);
    }

    public virtual async Task should_never_reissue_a_generation_granted_by_a_sweep_handler_that_threw()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await SeedExpiredAsync(host, kind, 1);
        long? rolledBack = null;

        // The sweep hands its own unit to the handler; a grant there must be refused where the provider refuses
        // enlisted grants, or else rolled back with the unit and never issued again.
        var swept = await host.Leases.SweepExpiredAsync(
            kind,
            async (_, unit, ct) =>
            {
                var granted = await unit.Leases.GrantAsync(kind, resource, LongDuration, ct);
                rolledBack = granted.Lease!.Generation;

                throw new InvalidOperationException("handler failed after granting");
            },
            limit: 10,
            AbortToken
        );

        var failure = swept.Failures.Should().ContainSingle().Subject;
        var next = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        next.Status.Should().Be(LeaseGrantStatus.Granted);

        if (Fixture.SupportsEnlistedGrant)
        {
            failure.Exception.Should().BeOfType<InvalidOperationException>();
            next.Lease!.Generation.Should()
                .BeGreaterThan(rolledBack!.Value, "a rolled-back generation is never issued again");
        }
        else
        {
            failure.Exception.Should().BeOfType<NotSupportedException>();
            rolledBack.Should().BeNull("the refused grant drew no generation");
        }
    }

    public virtual async Task should_not_reclaim_an_always_throwing_lease_within_one_sweep_call()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var generations = await SeedExpiredAsync(host, kind, 10);
        var poisoned = generations.Keys.ElementAt(4);
        var calls = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        var result = await host.Leases.SweepExpiredAsync(
            kind,
            (lease, _, _) =>
            {
                calls.AddOrUpdate(lease.Resource, 1, static (_, count) => count + 1);

                return string.Equals(lease.Resource, poisoned, StringComparison.Ordinal)
                    ? throw new InvalidOperationException("always fails")
                    : ValueTask.CompletedTask;
            },
            limit: 100,
            AbortToken
        );

        result.Handled.Should().HaveCount(9);
        result.Handled.Select(static l => l.Resource).Should().NotContain(poisoned);
        result.Failures.Should().ContainSingle().Which.Lease.Resource.Should().Be(poisoned);
        calls.Should().HaveCount(10);
        calls.Values.Should().AllSatisfy(count => count.Should().Be(1));
    }

    public virtual async Task should_sweep_only_the_requested_kind()
    {
        var (kindA, kindB) = (CreateKind(), CreateKind());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var leasesA = await SeedExpiredAsync(host, kindA, 2);
        var leasesB = await SeedExpiredAsync(host, kindB, 2);

        var result = await host.Leases.SweepExpiredAsync(kindA, (_, _, _) => ValueTask.CompletedTask, 100, AbortToken);

        result.Handled.Select(static l => l.Resource).Should().BeEquivalentTo(leasesA.Keys);
        result.Handled.Should().AllSatisfy(l => l.Kind.Should().Be(kindA));

        foreach (var resource in leasesB.Keys)
        {
            (await Fixture.ReadLeaseAsync(HostKey(kindB, resource), AbortToken))!
                .State.Should()
                .Be(StoredLeaseState.Active);
        }
    }

    public virtual async Task should_purge_only_old_ended_leases_and_never_reissue_a_generation()
    {
        var (kind, otherKind) = (CreateKind(), CreateKind());
        var age = TimeSpan.FromHours(2);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var oldSettled = CreateResource();
        var settled = await host.Leases.GrantAsync(kind, oldSettled, LongDuration, AbortToken);
        await host.Leases.SettleAsync(settled.Lease!, AbortToken);
        await Fixture.ShiftIntoPastAsync(HostKey(kind, oldSettled), age, AbortToken);

        var oldReleased = CreateResource();
        var released = await host.Leases.GrantAsync(kind, oldReleased, LongDuration, AbortToken);
        await host.Leases.ReleaseAsync(released.Lease!, AbortToken);
        await Fixture.ShiftIntoPastAsync(HostKey(kind, oldReleased), age, AbortToken);

        var freshSettled = CreateResource();
        var fresh = await host.Leases.GrantAsync(kind, freshSettled, LongDuration, AbortToken);
        await host.Leases.SettleAsync(fresh.Lease!, AbortToken);

        var oldActive = CreateResource();
        await host.Leases.GrantAsync(kind, oldActive, LongDuration, AbortToken);
        await Fixture.ShiftIntoPastAsync(HostKey(kind, oldActive), age, AbortToken);

        var otherResource = CreateResource();
        var other = await host.Leases.GrantAsync(otherKind, otherResource, LongDuration, AbortToken);
        await host.Leases.SettleAsync(other.Lease!, AbortToken);
        await Fixture.ShiftIntoPastAsync(HostKey(otherKind, otherResource), age, AbortToken);

        var deleted = await host.Leases.PurgeAsync(kind, TimeSpan.FromHours(1), AbortToken);

        deleted.Should().Be(2);
        (await Fixture.ReadLeaseAsync(HostKey(kind, oldSettled), AbortToken)).Should().BeNull();
        (await Fixture.ReadLeaseAsync(HostKey(kind, oldReleased), AbortToken)).Should().BeNull();
        (await Fixture.ReadLeaseAsync(HostKey(kind, freshSettled), AbortToken)).Should().NotBeNull();
        (await Fixture.ReadLeaseAsync(HostKey(kind, oldActive), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Active, "an expired but active lease is the sweep's, not purge's");
        (await Fixture.ReadLeaseAsync(HostKey(otherKind, otherResource), AbortToken)).Should().NotBeNull();

        var regranted = await host.Leases.GrantAsync(kind, oldSettled, LongDuration, AbortToken);

        regranted.Status.Should().Be(LeaseGrantStatus.Granted);
        regranted.Lease!.Generation.Should().BeGreaterThan(other.Lease!.Generation);
        (await host.Leases.SettleAsync(settled.Lease!, AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Stale, "the purged attempt's generation never matches the new row");
    }

    public virtual async Task should_purge_nothing_without_failing_when_the_age_exceeds_the_timestamp_range()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var resource = CreateResource();
        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await host.Leases.SettleAsync(granted.Lease!, AbortToken);

        var deleted = await host.Leases.PurgeAsync(kind, TimeSpan.MaxValue, AbortToken);

        deleted.Should().Be(0, "no lease can have ended further back than the timestamp range reaches");
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken)).Should().NotBeNull();
    }

    #endregion

    #region Progress and takeover count

    public virtual async Task should_return_the_last_renewed_progress_to_a_takeover()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var first = Progress("exports.cursor/v1", 1, 2, 3);
        var last = new LeaseProgress(
            Enumerable.Range(0, FencingFieldLimits.ProgressMaxBytes).Select(static i => (byte)i).ToArray(),
            new string('c', FencingFieldLimits.ProgressContractMaxLength)
        );

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        granted.TakeoverCount.Should().Be(0);
        granted.Progress.Should().BeNull("a new lease has no earlier attempt to resume");

        (await host.Leases.RenewAsync(granted.Lease!, LongDuration, first, AbortToken)).IsRenewed.Should().BeTrue();
        (await host.Leases.RenewAsync(granted.Lease!, LongDuration, last, AbortToken)).IsRenewed.Should().BeTrue();
        (await host.Leases.RenewAsync(granted.Lease!, LongDuration, AbortToken))
            .IsRenewed.Should()
            .BeTrue("a renewal without progress keeps what was recorded");

        await ExpireAsync(HostKey(kind, resource));
        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        takeover.Status.Should().Be(LeaseGrantStatus.Takeover);
        takeover.TakeoverCount.Should().Be(1);
        ShouldCarry(takeover.Progress, last);
    }

    public virtual async Task should_not_let_a_stale_or_expired_renewal_overwrite_the_progress()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var recorded = Progress("exports.cursor/v1", 1);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await host.Leases.RenewAsync(first.Lease!, LongDuration, recorded, AbortToken);
        await ExpireAsync(key);

        (await host.Leases.RenewAsync(first.Lease!, LongDuration, Progress("exports.cursor/v1", 2), AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Expired);

        var second = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        second.Status.Should().Be(LeaseGrantStatus.Takeover);
        ShouldCarry(second.Progress, recorded);

        (await host.Leases.RenewAsync(first.Lease!, LongDuration, Progress("exports.cursor/v1", 3), AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Stale);

        await ExpireAsync(key);
        var third = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        third.Status.Should().Be(LeaseGrantStatus.Takeover);
        ShouldCarry(third.Progress, recorded, "no later attempt recorded any");
    }

    // Found by the differential oracle: call validation accepted these, then PostgreSQL failed the statement on NUL
    // (22021) and Npgsql's UTF-8 encoder refused an unpaired surrogate, while SqlClient sent the surrogate as U+FFFD, so
    // SQL Server stored a different key (handed back to a sweep as a resource naming no lease the caller holds) and
    // would merge any two keys that differ only in which lone surrogate they carry.
    public virtual async Task should_refuse_a_key_no_provider_stores_unchanged_before_any_write()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        foreach (var unportable in (string[])["x\u0000y", "lone\ud800", "\udc00lone"])
        {
            var badResource = async () =>
                await host.Leases.GrantAsync(kind, resource + unportable, LongDuration, AbortToken);
            var badKind = async () =>
                await host.Leases.GrantAsync(kind + unportable, resource, LongDuration, AbortToken);
            var badLease = async () =>
                await host.Leases.SettleAsync(new FencedLease(null, kind, resource + unportable, 1), AbortToken);

            await badResource.Should().ThrowAsync<ArgumentException>();
            await badKind.Should().ThrowAsync<ArgumentException>();
            await badLease.Should().ThrowAsync<ArgumentException>();

            using (host.CurrentTenant.Change("tenant" + unportable))
            {
                var badTenant = async () => await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
                await badTenant.Should().ThrowAsync<ArgumentException>();
            }
        }

        // Nothing reached the store: the portable key is still free.
        (await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken))
            .Status.Should()
            .Be(LeaseGrantStatus.Granted);
    }

    public virtual async Task should_reject_oversized_progress_before_any_write()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var recorded = Progress("exports.cursor/v1", 7);
        var oversized = new LeaseProgress(new byte[FencingFieldLimits.ProgressMaxBytes + 1], "exports.cursor/v1");

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await host.Leases.RenewAsync(granted.Lease!, LongDuration, recorded, AbortToken);
        var before = await Fixture.ReadLeaseAsync(key, AbortToken);

        var autonomous = async () => await host.Leases.RenewAsync(granted.Lease!, LongDuration, oversized, AbortToken);
        await autonomous.Should().ThrowAsync<ArgumentException>().WithParameterName("progress");

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            var enlisted = async () =>
                await unit.Unit.Leases.RenewAsync(granted.Lease!, LongDuration, oversized, AbortToken);

            await enlisted.Should().ThrowAsync<ArgumentException>().WithParameterName("progress");
            unit.Unit.IsRetryPrevented.Should().BeFalse("a refused call never marks the unit");
            await unit.CommitAsync(AbortToken);
        }

        (await Fixture.ReadLeaseAsync(key, AbortToken)).Should().Be(before, "nothing was written");

        await ExpireAsync(key);
        ShouldCarry((await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken)).Progress, recorded);
    }

    public virtual async Task should_record_enlisted_progress_only_when_the_unit_commits()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var committed = Progress("exports.cursor/v1", 1);

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            (await unit.Unit.Leases.RenewAsync(granted.Lease!, LongDuration, committed, AbortToken))
                .IsRenewed.Should()
                .BeTrue();
            await unit.CommitAsync(AbortToken);
        }

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            (
                await unit.Unit.Leases.RenewAsync(
                    granted.Lease!,
                    LongDuration,
                    Progress("exports.cursor/v1", 2),
                    AbortToken
                )
            )
                .IsRenewed.Should()
                .BeTrue();
            await unit.RollbackAsync();
        }

        await ExpireAsync(HostKey(kind, resource));
        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        ShouldCarry(takeover.Progress, committed, "the rolled-back renewal never happened");
    }

    public virtual async Task should_count_consecutive_takeovers_and_reset_on_settle_or_release()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        (await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken)).TakeoverCount.Should().Be(0);
        await ExpireAsync(key);
        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        var held = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await ExpireAsync(key);
        var second = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        first.Status.Should().Be(LeaseGrantStatus.Takeover);
        first.TakeoverCount.Should().Be(1);
        held.Status.Should().Be(LeaseGrantStatus.Held);
        held.TakeoverCount.Should().Be(1, "a held grant reports the live holder's count");
        second.Status.Should().Be(LeaseGrantStatus.Takeover);
        second.TakeoverCount.Should().Be(2);

        await host.Leases.RenewAsync(second.Lease!, LongDuration, Progress("exports.cursor/v1", 1), AbortToken);
        (await host.Leases.SettleAsync(second.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
        var afterSettle = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        afterSettle.Status.Should().Be(LeaseGrantStatus.Granted);
        afterSettle.TakeoverCount.Should().Be(0);
        afterSettle.Progress.Should().BeNull("a settled lease's work is finished");

        await ExpireAsync(key);
        var third = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        third.TakeoverCount.Should().Be(1);
        await host.Leases.RenewAsync(third.Lease!, LongDuration, Progress("exports.cursor/v1", 2), AbortToken);
        (await host.Leases.ReleaseAsync(third.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Released);
        var afterRelease = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        afterRelease.Status.Should().Be(LeaseGrantStatus.Granted);
        afterRelease.TakeoverCount.Should().Be(0);
        afterRelease.Progress.Should().BeNull("a released lease gave its work up");
    }

    public virtual async Task should_hand_the_sweep_the_progress_and_count_an_abandoned_lease_once()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var recorded = Progress("exports.cursor/v1", 4, 2);

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await host.Leases.RenewAsync(granted.Lease!, LongDuration, recorded, AbortToken);
        await ExpireAsync(key);

        var swept = await host.Leases.SweepExpiredAsync(kind, (_, _, _) => ValueTask.CompletedTask, 10, AbortToken);
        var abandoned = swept.Handled.Should().ContainSingle().Subject;

        abandoned.Generation.Should().Be(granted.Lease!.Generation);
        abandoned.TakeoverCount.Should().Be(1);
        ShouldCarry(abandoned.Progress, recorded);

        var resumed = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        resumed.Status.Should().Be(LeaseGrantStatus.Granted);
        resumed.TakeoverCount.Should().Be(1, "the sweep already counted the abandonment");
        ShouldCarry(resumed.Progress, recorded, "the grant resumes the abandoned attempt");

        await ExpireAsync(key);
        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        takeover.Status.Should().Be(LeaseGrantStatus.Takeover);
        takeover.TakeoverCount.Should().Be(2);
        ShouldCarry(takeover.Progress, recorded);
    }

    #endregion

    #region Takeover after sweep, status, enlisted claims

    public virtual async Task should_report_an_expired_attempt_and_change_nothing_when_the_grant_waits_for_the_sweep()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);
        var held = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);

        first.Status.Should().Be(LeaseGrantStatus.Granted);
        held.Status.Should().Be(LeaseGrantStatus.Held, "a live attempt is held whatever the takeover choice");

        await ExpireAsync(key);
        var before = await Fixture.ReadLeaseAsync(key, AbortToken);
        var expired = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);

        expired.Status.Should().Be(LeaseGrantStatus.Expired);
        expired.IsAcquired.Should().BeFalse();
        expired.Lease.Should().BeNull();
        expired.HolderGeneration.Should().Be(first.Lease!.Generation);
        expired.ExpiresAt.Should().BeCloseTo(before!.ExpiresAt, TimeSpan.FromMicroseconds(1));
        expired.Progress.Should().BeNull();
        (await Fixture.ReadLeaseAsync(key, AbortToken)).Should().Be(before, "a refused grant writes nothing");

        var swept = await host.Leases.SweepExpiredAsync(kind, (_, _, _) => ValueTask.CompletedTask, 10, AbortToken);
        swept.Handled.Should().ContainSingle().Which.Generation.Should().Be(first.Lease.Generation);

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);

        granted.Status.Should().Be(LeaseGrantStatus.Granted, "the sweep ended the expired attempt");
        granted.Lease!.Generation.Should().BeGreaterThan(first.Lease.Generation);
        (await host.Leases.GetStatusAsync(first.Lease, AbortToken)).Should().Be(LeaseFenceStatus.Stale);
    }

    public virtual async Task should_refuse_an_enlisted_grant_over_an_expired_attempt_when_it_waits_for_the_sweep()
    {
        _SkipUnlessEnlistedGrant();
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await ExpireAsync(key);
        var before = await Fixture.ReadLeaseAsync(key, AbortToken);

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        var expired = await unit.Unit.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            LeaseTakeover.AfterSweep,
            AbortToken
        );
        await unit.CommitAsync(AbortToken);

        expired.Status.Should().Be(LeaseGrantStatus.Expired);
        expired.HolderGeneration.Should().Be(first.Lease!.Generation);
        (await Fixture.ReadLeaseAsync(key, AbortToken)).Should().Be(before);
    }

    public virtual async Task should_grant_at_once_when_waiting_for_the_sweep_and_no_attempt_is_active()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);
        (await host.Leases.SettleAsync(first.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
        var second = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);
        (await host.Leases.ReleaseAsync(second.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Released);
        var third = await host.Leases.GrantAsync(kind, resource, LongDuration, LeaseTakeover.AfterSweep, AbortToken);

        first.Status.Should().Be(LeaseGrantStatus.Granted);
        second.Status.Should().Be(LeaseGrantStatus.Granted);
        third.Status.Should().Be(LeaseGrantStatus.Granted);
        second.Lease!.Generation.Should().BeGreaterThan(first.Lease!.Generation);
        third.Lease!.Generation.Should().BeGreaterThan(second.Lease.Generation);
    }

    public virtual async Task should_report_each_lease_state_by_the_database_clock_without_changing_it()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var (live, settled, released, abandoned) = (
            CreateResource(),
            CreateResource(),
            CreateResource(),
            CreateResource()
        );
        var liveLease = (await host.Leases.GrantAsync(kind, live, LongDuration, AbortToken)).Lease!;
        var settledLease = (await host.Leases.GrantAsync(kind, settled, LongDuration, AbortToken)).Lease!;
        var releasedLease = (await host.Leases.GrantAsync(kind, released, LongDuration, AbortToken)).Lease!;
        var abandonedLease = (await host.Leases.GrantAsync(kind, abandoned, LongDuration, AbortToken)).Lease!;

        await host.Leases.SettleAsync(settledLease, AbortToken);
        await host.Leases.ReleaseAsync(releasedLease, AbortToken);
        await ExpireAsync(HostKey(kind, abandoned));
        await host.Leases.SweepExpiredAsync(kind, (_, _, _) => ValueTask.CompletedTask, 10, AbortToken);

        var before = await Fixture.ReadLeaseAsync(HostKey(kind, live), AbortToken);
        (await host.Leases.GetStatusAsync(liveLease, AbortToken)).Should().Be(LeaseFenceStatus.Current);
        (await Fixture.ReadLeaseAsync(HostKey(kind, live), AbortToken))
            .Should()
            .Be(before, "a status read writes nothing");

        (await host.Leases.GetStatusAsync(settledLease, AbortToken)).Should().Be(LeaseFenceStatus.Settled);
        (await host.Leases.GetStatusAsync(releasedLease, AbortToken)).Should().Be(LeaseFenceStatus.Released);
        (await host.Leases.GetStatusAsync(abandonedLease, AbortToken)).Should().Be(LeaseFenceStatus.Abandoned);
        (await host.Leases.GetStatusAsync(liveLease with { Resource = CreateResource() }, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Stale, "a lease with no row was purged or never granted");

        await ExpireAsync(HostKey(kind, live));
        (await host.Leases.GetStatusAsync(liveLease, AbortToken)).Should().Be(LeaseFenceStatus.Expired);

        var takeover = await host.Leases.GrantAsync(kind, live, LongDuration, AbortToken);
        (await host.Leases.GetStatusAsync(liveLease, AbortToken)).Should().Be(LeaseFenceStatus.Stale);
        (await host.Leases.GetStatusAsync(takeover.Lease!, AbortToken)).Should().Be(LeaseFenceStatus.Current);
    }

    public virtual async Task should_read_the_status_on_the_callers_connection_and_hand_it_back_as_passed()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var first = await host.Leases.GrantAsync(kind, resource, ShortDuration, AbortToken);
        await using var connection = Fixture.CreateConnection();

        (await host.Leases.GetStatusAsync(first.Lease!, connection, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Current);
        connection.State.Should().Be(ConnectionState.Closed, "a connection passed closed is closed again");

        await connection.OpenAsync(AbortToken);
        await ExpireAsync(HostKey(kind, resource));

        (await host.Leases.GetStatusAsync(first.Lease!, connection, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Expired);
        connection.State.Should().Be(ConnectionState.Open, "a connection passed open stays open");

        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        (await host.Leases.GetStatusAsync(first.Lease!, connection, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Stale);
        (await host.Leases.GetStatusAsync(first.Lease!, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Stale, "a replaced generation reads stale on every path");
        (await host.Leases.GetStatusAsync(takeover.Lease!, connection, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Current);
    }

    public virtual async Task should_read_the_status_inside_the_callers_transaction_and_refuse_a_foreign_one()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await using var connection = Fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);

        (await host.Leases.GetStatusAsync(granted.Lease!, connection, transaction, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Current);

        await using var other = Fixture.CreateConnection();
        var foreign = async () => await host.Leases.GetStatusAsync(granted.Lease!, other, transaction, AbortToken);

        await foreign.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not an open transaction*");
        await transaction.RollbackAsync(AbortToken);
    }

    public virtual async Task should_refuse_a_callers_connection_to_another_database_before_any_statement()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await using var connection = Fixture.CreateOtherDatabaseConnection();

        var act = async () =>
            await host.Leases.GetStatusAsync(granted.Lease!, connection, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        connection.State.Should().Be(ConnectionState.Closed, "the database is checked before the connection opens");
    }

    /// <summary>
    /// Holds the lease row under a write that outlives its own statement (an enlisted, uncommitted settlement), so only
    /// a provider whose status read never waits behind that lock returns before the unit ends.
    /// </summary>
    public virtual async Task should_read_the_status_without_waiting_on_an_uncommitted_write_to_the_lease()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        (await unit.Unit.Leases.SettleAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);

        var status = host.Leases.GetStatusAsync(granted.Lease!, AbortToken).AsTask();
        // The read must finish while the settlement is still uncommitted: a read that waits on the row lock never
        // completes before the commit below, so it times out here instead of passing.
        (await status.Bounded())
            .Should()
            .Be(LeaseFenceStatus.Current, "the settling transaction has not committed yet");

        await unit.CommitAsync(AbortToken);

        (await host.Leases.GetStatusAsync(granted.Lease!, AbortToken)).Should().Be(LeaseFenceStatus.Settled);
    }

    public virtual async Task should_claim_expired_leases_inside_the_callers_unit_and_keep_them_on_a_rollback()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var generations = await SeedExpiredAsync(host, kind, 2);
        var live = CreateResource();
        await host.Leases.GrantAsync(kind, live, LongDuration, AbortToken);

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            var first = await unit.Unit.Leases.ClaimExpiredAsync(kind, cancellationToken: AbortToken);
            var second = await unit.Unit.Leases.ClaimExpiredAsync(kind, first, AbortToken);
            var none = await unit.Unit.Leases.ClaimExpiredAsync(kind, second, AbortToken);

            new[] { first!.Resource, second!.Resource }.Should().BeEquivalentTo(generations.Keys);
            first.Generation.Should().Be(generations[first.Resource]);
            first.TakeoverCount.Should().Be(1, "claiming abandons the attempt and counts it once");
            none.Should().BeNull("a live lease is never claimed");

            await unit.RollbackAsync();
        }

        foreach (var resource in generations.Keys)
        {
            (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))!
                .State.Should()
                .Be(StoredLeaseState.Active, "a rolled-back claim leaves the lease expired and active");
        }

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            var claimed = await unit.Unit.Leases.ClaimExpiredAsync(kind, cancellationToken: AbortToken);
            await Fixture.WriteHandoffAsync(unit.Unit, claimed!, AbortToken);
            await unit.CommitAsync(AbortToken);

            (await Fixture.ReadLeaseAsync(HostKey(kind, claimed!.Resource), AbortToken))!
                .State.Should()
                .Be(StoredLeaseState.Abandoned);
            (await Fixture.ReadHandoffsAsync(kind, AbortToken))
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be(new LeaseHandoff("", claimed.Resource, claimed.Generation));
        }
    }

    public virtual async Task should_skip_a_lease_another_unit_claimed_when_claiming_inside_a_unit()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var generations = await SeedExpiredAsync(host, kind, 2);

        await using var first = await Fixture.BeginUnitAsync(host, AbortToken);
        var claimedFirst = await first.Unit.Leases.ClaimExpiredAsync(kind, cancellationToken: AbortToken);

        await using var second = await Fixture.BeginUnitAsync(host, AbortToken);
        var claimedSecond = await second
            .Unit.Leases.ClaimExpiredAsync(kind, cancellationToken: AbortToken)
            .AsTask()
            .WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);
        var nothingLeft = await second.Unit.Leases.ClaimExpiredAsync(kind, claimedSecond, AbortToken);

        claimedSecond!.Resource.Should().NotBe(claimedFirst!.Resource, "a claimed lease is skipped, not waited on");
        new[] { claimedFirst.Resource, claimedSecond.Resource }.Should().BeEquivalentTo(generations.Keys);
        nothingLeft.Should().BeNull();

        await second.CommitAsync(AbortToken);
        await first.CommitAsync(AbortToken);
    }

    #endregion

    #region Holder, current read, and revoke

    public virtual async Task should_return_the_held_lease_to_its_live_holder_and_hold_it_against_any_other()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var mine = new LeaseGrantOptions { Holder = "cluster/node-1@1" };

        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, mine, AbortToken);
        var before = await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken);
        var again = await host.Leases.GrantAsync(kind, resource, LongDuration, mine, AbortToken);
        var other = await host.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-2@1" },
            AbortToken
        );
        var anonymous = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        granted.Status.Should().Be(LeaseGrantStatus.Granted);
        again.Status.Should().Be(LeaseGrantStatus.Granted, "a retried grant by the live holder is the same grant");
        again.Lease.Should().Be(granted.Lease);
        again.ExpiresAt.Should().BeCloseTo(granted.ExpiresAt, TimeSpan.FromMicroseconds(1));
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))
            .Should()
            .Be(before, "a re-grant writes nothing and does not extend the expiry");
        other.Status.Should().Be(LeaseGrantStatus.Held);
        other.HolderGeneration.Should().Be(granted.Lease!.Generation);
        anonymous.Status.Should().Be(LeaseGrantStatus.Held, "a grant naming no holder never matches one");

        await ExpireAsync(HostKey(kind, resource));
        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, mine, AbortToken);

        takeover.Status.Should().Be(LeaseGrantStatus.Takeover, "an expired attempt is no longer the holder's");
        takeover.Lease!.Generation.Should().BeGreaterThan(granted.Lease.Generation);
        takeover.PreviousGeneration.Should().Be(granted.Lease.Generation);
    }

    public virtual async Task should_return_the_held_lease_to_its_live_holder_inside_a_unit()
    {
        _SkipUnlessEnlistedGrant();
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var mine = new LeaseGrantOptions { Holder = "cluster/node-1@1" };
        var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, mine, AbortToken);

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        var again = await unit.Unit.Leases.GrantAsync(kind, resource, LongDuration, mine, AbortToken);
        var other = await unit.Unit.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-2@1" },
            AbortToken
        );
        await unit.CommitAsync(AbortToken);

        again.Status.Should().Be(LeaseGrantStatus.Granted);
        again.Lease.Should().Be(granted.Lease);
        again.ExpiresAt.Should().BeCloseTo(granted.ExpiresAt, TimeSpan.FromMicroseconds(1));
        other.Status.Should().Be(LeaseGrantStatus.Held);
        (await host.Leases.GetStatusAsync(granted.Lease!, AbortToken)).Should().Be(LeaseFenceStatus.Current);
    }

    public virtual async Task should_read_the_current_attempt_with_its_holder_by_the_database_clock()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        (await host.Leases.GetCurrentAsync(kind, resource, AbortToken)).Should().BeNull("the key has no row");

        var granted = await host.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-1@1" },
            AbortToken
        );
        var before = await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken);
        var live = await host.Leases.GetCurrentAsync(kind, resource, AbortToken);

        live.Should().NotBeNull();
        live!.Lease.Should().Be(granted.Lease);
        live.Status.Should().Be(LeaseFenceStatus.Current);
        live.IsLive.Should().BeTrue();
        live.ExpiresAt.Should().BeCloseTo(granted.ExpiresAt, TimeSpan.FromMicroseconds(1));
        live.Holder.Should().Be("cluster/node-1@1");
        live.TakeoverCount.Should().Be(0);
        (await Fixture.ReadLeaseAsync(HostKey(kind, resource), AbortToken))
            .Should()
            .Be(before, "a current read writes nothing");

        await ExpireAsync(HostKey(kind, resource));
        var expired = await host.Leases.GetCurrentAsync(kind, resource, AbortToken);
        expired!.Status.Should().Be(LeaseFenceStatus.Expired);
        expired.IsLive.Should().BeFalse();

        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await host.Leases.SettleAsync(takeover.Lease!, AbortToken);
        var settled = await host.Leases.GetCurrentAsync(kind, resource, AbortToken);

        settled!.Lease.Should().Be(takeover.Lease);
        settled.Status.Should().Be(LeaseFenceStatus.Settled);
        settled.Holder.Should().BeNull("a grant naming no holder stores none");

        var tagged = CreateResource();
        var settledTagged = await host.Leases.GrantAsync(
            kind,
            tagged,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-1@1" },
            AbortToken
        );
        await host.Leases.SettleAsync(settledTagged.Lease!, AbortToken);
        var kept = await host.Leases.GetCurrentAsync(kind, tagged, AbortToken);

        kept!.Status.Should().Be(LeaseFenceStatus.Settled);
        kept.Holder.Should().Be("cluster/node-1@1", "a settled row still describes the generation it holds");
    }

    public virtual async Task should_read_the_current_attempt_on_the_callers_connection_and_hand_it_back_as_passed()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-1@1" },
            AbortToken
        );
        await using var connection = Fixture.CreateConnection();

        var closed = await host.Leases.GetCurrentAsync(kind, resource, connection, cancellationToken: AbortToken);
        connection.State.Should().Be(ConnectionState.Closed, "a connection passed closed is closed again");

        await connection.OpenAsync(AbortToken);
        var open = await host.Leases.GetCurrentAsync(kind, resource, connection, cancellationToken: AbortToken);
        connection.State.Should().Be(ConnectionState.Open, "a connection passed open stays open");

        var autonomous = await host.Leases.GetCurrentAsync(kind, resource, AbortToken);

        foreach (var snapshot in new[] { closed, open })
        {
            snapshot!.Lease.Should().Be(granted.Lease);
            snapshot.Status.Should().Be(LeaseFenceStatus.Current);
            snapshot.ExpiresAt.Should().Be(autonomous!.ExpiresAt);
            snapshot.Holder.Should().Be("cluster/node-1@1");
        }

        (await host.Leases.GetCurrentAsync(kind, CreateResource(), connection, cancellationToken: AbortToken))
            .Should()
            .BeNull();
    }

    public virtual async Task should_revoke_the_active_attempt_live_or_expired_and_nothing_else()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var (live, expired, settled) = (CreateResource(), CreateResource(), CreateResource());
        var liveLease = (await host.Leases.GrantAsync(kind, live, LongDuration, AbortToken)).Lease!;
        var expiredLease = (await host.Leases.GrantAsync(kind, expired, LongDuration, AbortToken)).Lease!;
        var settledLease = (await host.Leases.GrantAsync(kind, settled, LongDuration, AbortToken)).Lease!;
        await ExpireAsync(HostKey(kind, expired));
        await host.Leases.SettleAsync(settledLease, AbortToken);
        var settledRow = await Fixture.ReadLeaseAsync(HostKey(kind, settled), AbortToken);

        (await host.Leases.RevokeAsync(kind, live, AbortToken)).Should().Be(liveLease);
        (await host.Leases.RenewAsync(liveLease, LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Revoked, null));
        (await host.Leases.SettleAsync(liveLease, AbortToken)).Should().Be(LeaseSettlementStatus.Revoked);
        (await host.Leases.GetCurrentAsync(kind, live, AbortToken))!.Status.Should().Be(LeaseFenceStatus.Revoked);

        (await host.Leases.RevokeAsync(kind, expired, AbortToken)).Should().Be(expiredLease);
        (await host.Leases.GetStatusAsync(expiredLease, AbortToken)).Should().Be(LeaseFenceStatus.Revoked);

        (await host.Leases.RevokeAsync(kind, live, AbortToken)).Should().BeNull("the attempt already ended");
        (await host.Leases.RevokeAsync(kind, settled, AbortToken)).Should().BeNull("a settled attempt is not active");
        (await Fixture.ReadLeaseAsync(HostKey(kind, settled), AbortToken)).Should().Be(settledRow);
        (await host.Leases.RevokeAsync(kind, CreateResource(), AbortToken)).Should().BeNull("the key has no row");

        var next = await host.Leases.GrantAsync(kind, live, LongDuration, AbortToken);
        next.Status.Should().Be(LeaseGrantStatus.Granted, "a revoked attempt leaves the lease free at once");
        next.Lease!.Generation.Should().BeGreaterThan(liveLease.Generation);
    }

    public virtual async Task should_report_revoked_to_every_verb_of_the_revoked_generation_and_write_nothing()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-1@1" },
            AbortToken
        );
        var lease = granted.Lease!;

        (await host.Leases.RevokeAsync(kind, resource, AbortToken)).Should().Be(lease);
        var revoked = await Fixture.ReadLeaseAsync(key, AbortToken);

        revoked!.State.Should().Be(StoredLeaseState.Revoked, "a revocation is its own ending, not a release");
        revoked.EndedAt.Should().NotBeNull();

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            var fence = async () => await unit.Unit.Leases.FenceAsync(lease, AbortToken);
            var thrown = await fence.Should().ThrowAsync<StaleLeaseException>();
            thrown.Which.Reason.Should().Be(LeaseFenceStatus.Revoked);
        }

        (await host.Leases.GetStatusAsync(lease, AbortToken)).Should().Be(LeaseFenceStatus.Revoked);
        (await host.Leases.RenewAsync(lease, LongDuration, AbortToken))
            .Should()
            .Be(new LeaseRenewalResult(LeaseRenewalStatus.Revoked, null));
        (await host.Leases.SettleAsync(lease, AbortToken)).Should().Be(LeaseSettlementStatus.Revoked);
        (await host.Leases.ReleaseAsync(lease, AbortToken)).Should().Be(LeaseSettlementStatus.Revoked);
        (await Fixture.ReadLeaseAsync(key, AbortToken))
            .Should()
            .Be(revoked, "a refused verb on a revoked generation writes nothing");
    }

    public virtual async Task should_grant_a_new_generation_with_a_reset_takeover_count_after_a_revoke()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        var key = HostKey(kind, resource);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await ExpireAsync(key);
        var takeover = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        await host.Leases.RenewAsync(takeover.Lease!, LongDuration, Progress("exports.cursor/v1", 1), AbortToken);

        takeover.Status.Should().Be(LeaseGrantStatus.Takeover);
        takeover.TakeoverCount.Should().Be(1);

        (await host.Leases.RevokeAsync(kind, resource, AbortToken)).Should().Be(takeover.Lease);
        var next = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);

        next.Status.Should().Be(LeaseGrantStatus.Granted, "a revoked attempt is not taken over");
        next.Lease!.Generation.Should().BeGreaterThan(takeover.Lease!.Generation);
        next.PreviousGeneration.Should().BeNull();
        next.TakeoverCount.Should().Be(0, "a revocation resets the count as a release does");
        next.Progress.Should().BeNull("a revocation clears the progress as a release does");
    }

    public virtual async Task should_read_a_revoked_attempt_as_revoked_and_keep_its_holder()
    {
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(
            kind,
            resource,
            LongDuration,
            new LeaseGrantOptions { Holder = "cluster/node-1@1" },
            AbortToken
        );

        await host.Leases.RevokeAsync(kind, resource, AbortToken);
        var snapshot = await host.Leases.GetCurrentAsync(kind, resource, AbortToken);

        snapshot.Should().NotBeNull();
        snapshot!.Lease.Should().Be(granted.Lease);
        snapshot.Status.Should().Be(LeaseFenceStatus.Revoked);
        snapshot.IsLive.Should().BeFalse();
        snapshot.Holder.Should().Be("cluster/node-1@1", "a revoked row still names who held its generation");
        snapshot.TakeoverCount.Should().Be(0);
    }

    public virtual async Task should_purge_a_revoked_lease_older_than_the_threshold()
    {
        var kind = CreateKind();
        var age = TimeSpan.FromHours(2);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var (old, fresh) = (CreateResource(), CreateResource());
        await host.Leases.GrantAsync(kind, old, LongDuration, AbortToken);
        await host.Leases.RevokeAsync(kind, old, AbortToken);
        await Fixture.ShiftIntoPastAsync(HostKey(kind, old), age, AbortToken);
        await host.Leases.GrantAsync(kind, fresh, LongDuration, AbortToken);
        await host.Leases.RevokeAsync(kind, fresh, AbortToken);

        var deleted = await host.Leases.PurgeAsync(kind, TimeSpan.FromHours(1), AbortToken);

        deleted.Should().Be(1);
        (await Fixture.ReadLeaseAsync(HostKey(kind, old), AbortToken)).Should().BeNull();
        (await Fixture.ReadLeaseAsync(HostKey(kind, fresh), AbortToken))!.State.Should().Be(StoredLeaseState.Revoked);
    }

    #endregion

    #region Inspection

    public virtual async Task should_list_every_lease_of_a_kind_in_key_order_by_pages_without_repeat_or_gap()
    {
        var kind = CreateKind();
        var (tenantA, tenantB) = (CreateTenant("a"), CreateTenant("b"));
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var expected = new List<(string? Tenant, string Resource)>();

        foreach (var tenant in new[] { tenantB, null, tenantA })
        {
            using (host.CurrentTenant.Change(tenant))
            {
                foreach (var resource in new[] { "r-2", "r-1" })
                {
                    await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
                    expected.Add((tenant, resource));
                }
            }
        }

        // The host scope sorts first, then tenants and resources ordinally.
        var ordered = expected
            .OrderBy(static e => e.Tenant ?? "", StringComparer.Ordinal)
            .ThenBy(static e => e.Resource, StringComparer.Ordinal)
            .ToList();

        var query = new FencedLeaseQuery { Kind = kind };
        var listed = new List<LeaseSnapshot>();
        var sizes = new List<int>();
        FencedLeasePosition? after = null;

        do
        {
            var page = await host.Inspector.ListAsync(query, after, pageSize: 4, AbortToken);
            sizes.Add(page.Items.Count);
            listed.AddRange(page.Items);
            after = page.Next;
        } while (after is not null);

        sizes.Should().Equal(4, 2);
        listed.Select(static s => (s.Lease.TenantId, s.Lease.Resource)).Should().Equal(ordered);
        listed.Should().OnlyContain(s => s.Lease.Kind == kind && s.IsLive);

        var exact = await host.Inspector.ListAsync(query, after: null, pageSize: 6, AbortToken);
        exact.Items.Should().HaveCount(6);
        exact.Next.Should().BeNull("a page that holds the last lease has no next page");
    }

    public virtual async Task should_partition_listed_and_counted_leases_into_live_expired_and_ended_by_the_database_clock()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var (live, expired, settled, released, revoked, abandoned) = (
            "live",
            "expired",
            "settled",
            "released",
            "revoked",
            "abandoned"
        );
        // The sweep runs before the expired lease expires, so it abandons only its own lease.
        await host.Leases.GrantAsync(kind, abandoned, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, abandoned));
        await _SweepOneAsync(host, kind, abandoned);
        await host.Leases.GrantAsync(kind, live, LongDuration, AbortToken);
        await host.Leases.GrantAsync(kind, expired, LongDuration, AbortToken);
        await ExpireAsync(HostKey(kind, expired));
        await host.Leases.SettleAsync(
            (await host.Leases.GrantAsync(kind, settled, LongDuration, AbortToken)).Lease!,
            AbortToken
        );
        await host.Leases.ReleaseAsync(
            (await host.Leases.GrantAsync(kind, released, LongDuration, AbortToken)).Lease!,
            AbortToken
        );
        await host.Leases.GrantAsync(kind, revoked, LongDuration, AbortToken);
        await host.Leases.RevokeAsync(kind, revoked, AbortToken);

        async Task<string[]> list(FencedLeaseStateFilter state)
        {
            var page = await host.Inspector.ListAsync(
                new FencedLeaseQuery { Kind = kind, State = state },
                after: null,
                pageSize: 50,
                AbortToken
            );

            return [.. page.Items.Select(static s => s.Lease.Resource)];
        }

        (await list(FencedLeaseStateFilter.Live)).Should().Equal(live);
        (await list(FencedLeaseStateFilter.Expired)).Should().Equal(expired);
        (await list(FencedLeaseStateFilter.Ended)).Should().BeEquivalentTo(settled, released, revoked, abandoned);
        (await list(FencedLeaseStateFilter.All)).Should().HaveCount(6);

        var all = await host.Inspector.ListAsync(new FencedLeaseQuery { Kind = kind }, null, 50, AbortToken);
        var byResource = all.Items.ToDictionary(static s => s.Lease.Resource, StringComparer.Ordinal);
        byResource[live]
            .Should()
            .Match<LeaseSnapshot>(s => s.IsLive && !s.IsExpired && !s.IsEnded && s.EndedAt == null);
        byResource[expired]
            .Should()
            .Match<LeaseSnapshot>(s => s.IsExpired && s.Status == LeaseFenceStatus.Expired && s.EndedAt == null);
        byResource[settled].Status.Should().Be(LeaseFenceStatus.Settled);
        byResource[released].Status.Should().Be(LeaseFenceStatus.Released);
        byResource[revoked].Status.Should().Be(LeaseFenceStatus.Revoked);
        byResource[abandoned].Status.Should().Be(LeaseFenceStatus.Abandoned);
        all.Items.Where(static s => s.IsEnded).Should().OnlyContain(static s => s.EndedAt != null);
        all.Items.Should().OnlyContain(static s => s.GrantedAt <= s.ExpiresAt);

        var counts = await host.Inspector.CountAsync(
            new FencedLeaseQuery { Kind = kind },
            cancellationToken: AbortToken
        );
        counts.Live.Should().Be(new LeaseCount(1, Capped: false));
        counts.Expired.Should().Be(new LeaseCount(1, Capped: false));
        counts.Ended.Should().Be(new LeaseCount(4, Capped: false));
        counts.Total.Should().Be(new LeaseCount(6, Capped: false));

        var endedOnly = await host.Inspector.CountAsync(
            new FencedLeaseQuery { Kind = kind, State = FencedLeaseStateFilter.Ended },
            cancellationToken: AbortToken
        );
        endedOnly.Should().Be(new FencedLeaseCounts(default, default, new LeaseCount(4, Capped: false)));
    }

    public virtual async Task should_judge_listed_expiry_by_the_database_clock_whatever_the_application_clock()
    {
        foreach (var skew in new[] { TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(10) })
        {
            var kind = CreateKind();
            await using var host = await Fixture.CreateHostAsync(
                timeProvider: new SkewedTimeProvider(skew),
                cancellationToken: AbortToken
            );

            await host.Leases.GrantAsync(kind, "live", LongDuration, AbortToken);
            await host.Leases.GrantAsync(kind, "expired", LongDuration, AbortToken);
            await ExpireAsync(HostKey(kind, "expired"));

            var page = await host.Inspector.ListAsync(new FencedLeaseQuery { Kind = kind }, null, 10, AbortToken);
            var counts = await host.Inspector.CountAsync(
                new FencedLeaseQuery { Kind = kind },
                cancellationToken: AbortToken
            );

            page.Items.Single(static s => s.Lease.Resource == "live").IsLive.Should().BeTrue($"under a {skew} skew");
            page.Items.Single(static s => s.Lease.Resource == "expired")
                .IsExpired.Should()
                .BeTrue($"under a {skew} skew");
            counts.Live.Count.Should().Be(1);
            counts.Expired.Count.Should().Be(1);
        }
    }

    public virtual async Task should_filter_by_tenant_or_the_host_scope_and_get_by_an_explicit_tenant()
    {
        var kind = CreateKind();
        var (tenantA, tenantB) = (CreateTenant("a"), CreateTenant("b"));
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var hostLease = (await host.Leases.GrantAsync(kind, "shared", LongDuration, AbortToken)).Lease!;
        FencedLease tenantLease;

        using (host.CurrentTenant.Change(tenantA))
        {
            tenantLease = (await host.Leases.GrantAsync(kind, "shared", LongDuration, AbortToken)).Lease!;
        }

        using (host.CurrentTenant.Change(tenantB))
        {
            await host.Leases.GrantAsync(kind, "other", LongDuration, AbortToken);
        }

        async Task<FencedLease[]> list(LeaseTenantFilter tenant)
        {
            var page = await host.Inspector.ListAsync(
                new FencedLeaseQuery { Kind = kind, Tenant = tenant },
                null,
                10,
                AbortToken
            );

            return [.. page.Items.Select(static s => s.Lease)];
        }

        (await list(LeaseTenantFilter.Any)).Should().HaveCount(3);
        (await list(LeaseTenantFilter.Host)).Should().Equal(hostLease);
        (await list(LeaseTenantFilter.Of(null))).Should().Equal(hostLease);
        (await list(LeaseTenantFilter.Of(tenantA))).Should().Equal(tenantLease);

        (
            await host.Inspector.CountAsync(
                new FencedLeaseQuery { Kind = kind, Tenant = LeaseTenantFilter.Of(tenantB) },
                cancellationToken: AbortToken
            )
        )
            .Total.Should()
            .Be(new LeaseCount(1, Capped: false));

        using (host.CurrentTenant.Change(tenantB))
        {
            // The inspector never reads the current tenant.
            (await host.Inspector.GetAsync(tenantA, kind, "shared", AbortToken))!
                .Lease.Should()
                .Be(tenantLease);
            (await host.Inspector.GetAsync(null, kind, "shared", AbortToken))!.Lease.Should().Be(hostLease);
        }

        (await host.Inspector.GetAsync(tenantB, kind, "shared", AbortToken))
            .Should()
            .BeNull("tenant b has no such lease");
        (await host.Inspector.GetAsync(tenantA, kind, "shared", AbortToken))!.IsLive.Should().BeTrue();
    }

    public virtual async Task should_match_the_resource_prefix_ordinally_and_with_every_character_literal()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        string[] resources =
        [
            "Export/1",
            "export/1",
            "export/2",
            "expo",
            "exp%x",
            "exp_y",
            "ex[p]z",
            "ex*p?",
            @"ex\q",
            "exq",
        ];

        foreach (var resource in resources)
        {
            await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        }

        async Task<string[]> matching(string prefix)
        {
            var page = await host.Inspector.ListAsync(
                new FencedLeaseQuery { Kind = kind, ResourcePrefix = prefix },
                null,
                50,
                AbortToken
            );

            return [.. page.Items.Select(static s => s.Lease.Resource)];
        }

        (await matching("export/")).Should().Equal("export/1", "export/2");
        (await matching("Export/")).Should().Equal("Export/1");
        (await matching("exp%")).Should().Equal("exp%x");
        (await matching("exp_")).Should().Equal("exp_y");
        (await matching("ex[")).Should().Equal("ex[p]z");
        (await matching("ex*")).Should().Equal("ex*p?");
        (await matching(@"ex\")).Should().Equal(@"ex\q");
        (await matching("export/1")).Should().Equal("export/1");
        (await matching("export/1-longer")).Should().BeEmpty();
        (await matching("")).Should().HaveCount(resources.Length, "an empty prefix matches every resource");

        (
            await host.Inspector.CountAsync(
                new FencedLeaseQuery { Kind = kind, ResourcePrefix = "exp_" },
                cancellationToken: AbortToken
            )
        )
            .Live.Should()
            .Be(new LeaseCount(1, Capped: false));
    }

    public virtual async Task should_report_progress_size_and_contract_without_its_bytes()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var granted = await host.Leases.GrantAsync(kind, "with-progress", LongDuration, AbortToken);
        await host.Leases.RenewAsync(
            granted.Lease!,
            LongDuration,
            Progress("export.cursor/v1", 1, 2, 3, 4, 5),
            AbortToken
        );
        await host.Leases.GrantAsync(kind, "without-progress", LongDuration, AbortToken);

        var withProgress = await host.Inspector.GetAsync(null, kind, "with-progress", AbortToken);
        var stored = await Fixture.ReadLeaseAsync(HostKey(kind, "with-progress"), AbortToken);

        withProgress!.ProgressSizeBytes.Should().Be(5);
        withProgress.ProgressContract.Should().Be("export.cursor/v1");
        withProgress.GrantedAt.Should().BeCloseTo(stored!.GrantedAt, TimeSpan.FromMicroseconds(1));
        withProgress.ExpiresAt.Should().BeCloseTo(stored.ExpiresAt, TimeSpan.FromMicroseconds(1));

        var page = await host.Inspector.ListAsync(new FencedLeaseQuery { Kind = kind }, null, 10, AbortToken);
        page.Items.Single(static s => s.Lease.Resource == "with-progress").Should().Be(withProgress);
        var without = page.Items.Single(static s => s.Lease.Resource == "without-progress");
        without.ProgressSizeBytes.Should().BeNull();
        without.ProgressContract.Should().BeNull();

        typeof(LeaseSnapshot)
            .GetProperties()
            .Select(static p => p.PropertyType)
            .Should()
            .NotContain(
                [typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>), typeof(LeaseProgress)],
                "an operator view never carries the progress bytes"
            );

        var current = await host.Leases.GetCurrentAsync(kind, "with-progress", AbortToken);
        current.Should().Be(withProgress, "the current read and the inspector read the same attempt");
    }

    public virtual async Task should_list_the_kinds_in_use_in_ordinal_order_until_their_rows_are_purged()
    {
        var stem = CreateKind();
        var (upper, lower, purged) = ($"{stem}-B", $"{stem}-a", $"{stem}-c");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        await host.Leases.GrantAsync(lower, "r", LongDuration, AbortToken);
        await host.Leases.GrantAsync(upper, "r", LongDuration, AbortToken);
        await host.Leases.SettleAsync(
            (await host.Leases.GrantAsync(purged, "r", LongDuration, AbortToken)).Lease!,
            AbortToken
        );

        var kinds = await host.Inspector.ListKindsAsync(AbortToken);
        kinds.Should().BeInAscendingOrder(StringComparer.Ordinal);
        kinds.Should().OnlyHaveUniqueItems();
        kinds.Where(k => k.StartsWith(stem, StringComparison.Ordinal)).Should().Equal(upper, lower, purged);

        (await host.Leases.PurgeAsync(purged, TimeSpan.Zero, AbortToken)).Should().Be(1);

        (await host.Inspector.ListKindsAsync(AbortToken))
            .Where(k => k.StartsWith(stem, StringComparison.Ordinal))
            .Should()
            .Equal(upper, lower);
    }

    public virtual async Task should_cap_each_state_count_on_its_own()
    {
        var kind = CreateKind();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        foreach (var resource in new[] { "l-1", "l-2", "l-3" })
        {
            await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
        }

        await host.Leases.ReleaseAsync(
            (await host.Leases.GrantAsync(kind, "e-1", LongDuration, AbortToken)).Lease!,
            AbortToken
        );
        await host.Leases.ReleaseAsync(
            (await host.Leases.GrantAsync(kind, "e-2", LongDuration, AbortToken)).Lease!,
            AbortToken
        );

        var capped = await host.Inspector.CountAsync(new FencedLeaseQuery { Kind = kind }, cap: 2, AbortToken);

        capped.Live.Should().Be(new LeaseCount(2, Capped: true));
        capped.Expired.Should().Be(new LeaseCount(0, Capped: false));
        capped.Ended.Should().Be(new LeaseCount(2, Capped: false), "a capped live count never hides the ended count");
        capped.Total.Should().Be(new LeaseCount(4, Capped: true));

        var exact = await host.Inspector.CountAsync(new FencedLeaseQuery { Kind = kind }, cap: 3, AbortToken);
        exact.Live.Should().Be(new LeaseCount(3, Capped: false));
    }

    public virtual async Task should_refuse_an_invalid_inspection_before_reading()
    {
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var query = new FencedLeaseQuery();

        await FluentActions
            .Awaiting(async () => await host.Inspector.ListAsync(null!, null, 10, AbortToken))
            .Should()
            .ThrowAsync<ArgumentNullException>();
        await FluentActions
            .Awaiting(async () => await host.Inspector.ListAsync(query, null, 0, AbortToken))
            .Should()
            .ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions
            .Awaiting(async () =>
                await host.Inspector.ListAsync(query, null, FencedLeasePage.MaxPageSize + 1, AbortToken)
            )
            .Should()
            .ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions
            .Awaiting(async () =>
                await host.Inspector.ListAsync(query with { State = (FencedLeaseStateFilter)99 }, null, 10, AbortToken)
            )
            .Should()
            .ThrowAsync<System.ComponentModel.InvalidEnumArgumentException>();
        await FluentActions
            .Awaiting(async () =>
                await host.Inspector.ListAsync(query with { Tenant = LeaseTenantFilter.Of("") }, null, 10, AbortToken)
            )
            .Should()
            .ThrowAsync<ArgumentException>("the empty tenant id is the stored host scope, never a tenant");
        await FluentActions
            .Awaiting(async () => await host.Inspector.ListAsync(query with { Kind = " " }, null, 10, AbortToken))
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(async () =>
                await host.Inspector.ListAsync(query, new FencedLeasePosition(null, "k", ""), 10, AbortToken)
            )
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(async () => await host.Inspector.GetAsync("", "k", "r", AbortToken))
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(async () => await host.Inspector.CountAsync(query, cap: 0, AbortToken))
            .Should()
            .ThrowAsync<ArgumentOutOfRangeException>();
    }

    private async Task _SweepOneAsync(LeasesHost host, string kind, string resource)
    {
        var swept = await host.Leases.SweepExpiredAsync(
            kind,
            (_, _, _) => ValueTask.CompletedTask,
            limit: 10,
            AbortToken
        );
        swept.Handled.Select(static l => l.Resource).Should().Equal(resource);
    }

    /// <summary>Returns a tenant id no other test uses; <paramref name="suffix" /> orders tenants of one test.</summary>
    protected static string CreateTenant(string suffix)
    {
        return $"tenant-{Guid.NewGuid():N}-{suffix}";
    }

    #endregion

    /// <summary>Returns a progress record over <paramref name="payload" />.</summary>
    protected static LeaseProgress Progress(string contract, params byte[] payload)
    {
        return new LeaseProgress(payload, contract);
    }

    /// <summary>Asserts <paramref name="actual" /> carries the bytes and contract of <paramref name="expected" />.</summary>
    protected static void ShouldCarry(LeaseProgress? actual, LeaseProgress expected, string because = "")
    {
        actual.Should().NotBeNull(because);
        actual!.Contract.Should().Be(expected.Contract, because);
        actual.Payload.ToArray().Should().Equal(expected.Payload.ToArray(), because);
    }

    /// <summary>The stored key of a host-scope lease.</summary>
    protected static LeaseKey HostKey(string kind, string resource)
    {
        return new LeaseKey("", kind, resource);
    }

    /// <summary>Moves a lease's expiry well into the database's past.</summary>
    protected Task ExpireAsync(LeaseKey key)
    {
        return Fixture.ShiftIntoPastAsync(key, LongDuration + TimeSpan.FromMinutes(1), AbortToken);
    }

    /// <summary>Grants <paramref name="count" /> host-scope leases of <paramref name="kind" /> and expires them.</summary>
    /// <returns>Each resource with its granted generation, in grant order.</returns>
    protected async Task<IReadOnlyDictionary<string, long>> SeedExpiredAsync(LeasesHost host, string kind, int count)
    {
        var generations = new Dictionary<string, long>(StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            var resource = CreateResource();
            var granted = await host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken);
            await ExpireAsync(HostKey(kind, resource));
            generations.Add(resource, granted.Lease!.Generation);
        }

        return generations;
    }

    /// <summary>Returns a lease kind no other test uses.</summary>
    protected static string CreateKind()
    {
        return $"kind-{Guid.NewGuid():N}";
    }

    /// <summary>Returns a resource name no other test uses.</summary>
    protected static string CreateResource()
    {
        return $"resource-{Guid.NewGuid():N}";
    }
}
