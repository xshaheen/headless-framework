// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

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
#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public abstract class LeasesConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ILeasesFixture
{
    /// <summary>A duration no test outlives, so a lease granted with it stays live unless a test ages it.</summary>
    protected static readonly TimeSpan LongDuration = TimeSpan.FromMinutes(5);

    /// <summary>The shortest duration the default options accept, for tests that let the database clock run out.</summary>
    protected static readonly TimeSpan ShortDuration = TimeSpan.FromSeconds(1);

    protected TFixture Fixture { get; } = fixture;

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
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        var granted = await unit.Unit.Leases.GrantAsync(kind, resource, ShortDuration, AbortToken);
        await unit.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        // The transaction started before the grant and stays open: a clock frozen at transaction start would still
        // call the lease live.
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
        var (kind, resource) = (CreateKind(), CreateResource());
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        var enlisted = await unit.Unit.Leases.GrantAsync(kind, resource, ShortDuration, AbortToken);
        var pending = host.Leases.GrantAsync(kind, resource, LongDuration, AbortToken).AsTask();

        // Held open past the enlisted lease's expiry, so the waiter takes it over once the unit commits.
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

        handled.Select(static l => l.Resource).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(generations.Keys);
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
