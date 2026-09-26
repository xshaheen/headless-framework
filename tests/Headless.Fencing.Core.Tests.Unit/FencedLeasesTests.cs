// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class FencedLeasesTests : TestBase
{
    private static readonly FencedLease _Lease = new(null, "job", "order-1", 7);
    private static readonly LeaseKey _Key = new("", "job", "order-1");

    [Fact]
    public async Task should_grant_autonomously_with_the_resolved_key()
    {
        // given
        var context = new FencingTestContext();
        context.Tenant.Id = "t1";
        var expected = LeaseGrantResult.Held(3, DateTimeOffset.UnixEpoch);
        context
            .Store.GrantAsync(new LeaseKey("t1", "job", "order-1"), FencingTestContext.Duration, AbortToken)
            .Returns(expected);

        // when
        var result = await context.Leases.GrantAsync("job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task should_forward_renew_settle_and_release_with_the_lease_key_and_generation()
    {
        // given
        var context = new FencingTestContext();
        context
            .Store.RenewAsync(_Key, 7, FencingTestContext.Duration, AbortToken)
            .Returns(new LeaseRenewalResult(LeaseRenewalStatus.Stale, null));
        context.Store.SettleAsync(_Key, 7, AbortToken).Returns(LeaseSettlementStatus.Expired);
        context.Store.ReleaseAsync(_Key, 7, AbortToken).Returns(LeaseSettlementStatus.Released);

        // when
        var renewal = await context.Leases.RenewAsync(_Lease, FencingTestContext.Duration, AbortToken);
        var settlement = await context.Leases.SettleAsync(_Lease, AbortToken);
        var release = await context.Leases.ReleaseAsync(_Lease, AbortToken);

        // then
        renewal.Status.Should().Be(LeaseRenewalStatus.Stale);
        settlement.Should().Be(LeaseSettlementStatus.Expired);
        release.Should().Be(LeaseSettlementStatus.Released);
    }

    [Fact]
    public async Task should_reject_a_duration_outside_the_bounds_before_the_store()
    {
        var context = new FencingTestContext();

        var grant = async () => await context.Leases.GrantAsync("job", "order-1", TimeSpan.Zero, AbortToken);
        var renew = async () => await context.Leases.RenewAsync(_Lease, TimeSpan.FromDays(3), AbortToken);

        await grant.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await renew.Should().ThrowAsync<ArgumentOutOfRangeException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_purge_by_kind_and_refuse_a_negative_age()
    {
        // given
        var context = new FencingTestContext();
        context.Store.PurgeAsync("job", TimeSpan.FromHours(1), AbortToken).Returns(4);

        // when
        var purged = await context.Leases.PurgeAsync("job", TimeSpan.FromHours(1), AbortToken);
        var negative = async () => await context.Leases.PurgeAsync("job", TimeSpan.FromSeconds(-1), AbortToken);

        // then
        purged.Should().Be(4);
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_hand_each_claimed_lease_to_the_handler_in_its_own_committed_unit()
    {
        // given
        var context = new FencingTestContext();
        var first = _Expired("a", 1);
        var second = _Expired("b", 2);
        var units = _QueueUnits(context, 3);
        _QueueClaims(context, first, second, null);
        var seen = new List<(ExpiredLease Lease, IUnitOfWork Unit)>();

        // when
        var result = await context.Leases.SweepExpiredAsync(
            "job",
            (lease, unit, _) =>
            {
                seen.Add((lease, unit));
                return ValueTask.CompletedTask;
            },
            limit: 10,
            AbortToken
        );

        // then
        result.Handled.Should().Equal(first, second);
        result.Failures.Should().BeEmpty();
        seen.Should().Equal((first, units[0].Unit), (second, units[1].Unit));
        await units[0].Unit.Received(1).CompleteAsync(CancellationToken.None);
        await units[1].Unit.Received(1).CompleteAsync(CancellationToken.None);
        await units[2].Unit.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        await units[2].Unit.Received(1).RollbackAsync();
    }

    [Fact]
    public async Task should_advance_the_cursor_past_every_visited_lease()
    {
        // given
        var context = new FencingTestContext();
        var first = _Expired("a", 1);
        var second = _Expired("b", 2);
        var units = _QueueUnits(context, 3);
        _QueueClaims(context, first, second, null);

        // when
        await context.Leases.SweepExpiredAsync(
            "job",
            (lease, _, _) => lease == first ? throw new InvalidOperationException("boom") : ValueTask.CompletedTask,
            limit: 10,
            AbortToken
        );

        // then — a failed lease still moves the cursor, so this call never re-claims it
        Received.InOrder(() =>
        {
            _ = context.Store.ClaimExpiredEnlistedAsync(units[0].Resource, "job", null, AbortToken);
            _ = context.Store.ClaimExpiredEnlistedAsync(units[1].Resource, "job", first, AbortToken);
            _ = context.Store.ClaimExpiredEnlistedAsync(units[2].Resource, "job", second, AbortToken);
        });
    }

    [Fact]
    public async Task should_roll_back_only_the_lease_whose_handler_threw_and_report_it()
    {
        // given
        var context = new FencingTestContext();
        var first = _Expired("a", 1);
        var second = _Expired("b", 2);
        var units = _QueueUnits(context, 3);
        _QueueClaims(context, first, second, null);
        var failure = new InvalidOperationException("boom");

        // when
        var result = await context.Leases.SweepExpiredAsync(
            "job",
            (lease, _, _) => lease == first ? throw failure : ValueTask.CompletedTask,
            limit: 10,
            AbortToken
        );

        // then
        result.Handled.Should().Equal(second);
        result.Failures.Should().ContainSingle().Which.Should().Be(new LeaseSweepFailure(first, failure));
        await units[0].Unit.DidNotReceiveWithAnyArgs().CompleteAsync(default);
        await units[0].Unit.Received(1).RollbackAsync();
        await units[1].Unit.Received(1).CompleteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task should_stop_at_the_limit_without_claiming_more()
    {
        // given
        var context = new FencingTestContext();
        var units = _QueueUnits(context, 3);
        _QueueClaims(context, _Expired("a", 1), _Expired("b", 2), _Expired("c", 3));

        // when
        var result = await context.Leases.SweepExpiredAsync(
            "job",
            static (_, _, _) => ValueTask.CompletedTask,
            limit: 2,
            AbortToken
        );

        // then
        result.Handled.Should().HaveCount(2);
        await context.Store.Received(2).BeginOwnedUnitAsync(AbortToken);
        units[2].Unit.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_propagate_the_callers_cancellation_from_a_handler()
    {
        // given
        var context = new FencingTestContext();
        var units = _QueueUnits(context, 1);
        _QueueClaims(context, _Expired("a", 1));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        // when
        var act = async () =>
            await context.Leases.SweepExpiredAsync(
                "job",
                (_, _, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return ValueTask.CompletedTask;
                },
                limit: 5,
                cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        await units[0].Unit.DidNotReceiveWithAnyArgs().CompleteAsync(default);
    }

    [Fact]
    public async Task should_validate_sweep_arguments_before_the_store()
    {
        var context = new FencingTestContext();

        var badLimit = async () =>
            await context.Leases.SweepExpiredAsync("job", static (_, _, _) => ValueTask.CompletedTask, 0, AbortToken);
        var badKind = async () =>
            await context.Leases.SweepExpiredAsync(" job", static (_, _, _) => ValueTask.CompletedTask, 1, AbortToken);
        var noHandler = async () => await context.Leases.SweepExpiredAsync("job", null!, 1, AbortToken);

        await badLimit.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await badKind.Should().ThrowAsync<ArgumentException>();
        await noHandler.Should().ThrowAsync<ArgumentNullException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    private static ExpiredLease _Expired(string resource, long generation)
    {
        return new ExpiredLease(null, "job", resource, generation, DateTimeOffset.UnixEpoch.AddSeconds(generation));
    }

    private List<(IUnitOfWork Unit, IRelationalUnitOfWorkResource Resource)> _QueueUnits(
        FencingTestContext context,
        int count
    )
    {
        var units = Enumerable.Range(0, count).Select(_ => FencingTestContext.ActiveUnit(isOwned: true)).ToList();
        var queue = new Queue<IUnitOfWork>(units.Select(static u => u.Unit));
        context
            .Store.BeginOwnedUnitAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult(queue.Dequeue()));

        return units;
    }

    private void _QueueClaims(FencingTestContext context, params ExpiredLease?[] claims)
    {
        var queue = new Queue<ExpiredLease?>(claims);
        context
            .Store.ClaimExpiredEnlistedAsync(
                Arg.Any<IRelationalUnitOfWorkResource>(),
                "job",
                Arg.Any<ExpiredLease?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => ValueTask.FromResult(queue.Dequeue()));
    }
}
