// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkLeasesFeatureTests : TestBase
{
    private static readonly FencedLease _Lease = new("t1", "job", "order-1", 7);
    private static readonly LeaseKey _Key = new("t1", "job", "order-1");

    [Fact]
    public async Task should_grant_on_the_unit_resource_with_the_resolved_key()
    {
        // given
        var context = new FencingTestContext();
        context.Tenant.Id = "t1";
        var (unit, resource) = FencingTestContext.ActiveUnit();
        var expected = LeaseGrantResult.Granted(_Lease, DateTimeOffset.UnixEpoch);
        context.Store.GrantEnlistedAsync(resource, _Key, FencingTestContext.Duration, AbortToken).Returns(expected);

        // when
        var result = await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        result.Should().BeSameAs(expected);
        await context.Store.DidNotReceiveWithAnyArgs().GrantAsync(default, default, AbortToken);
    }

    [Fact]
    public async Task should_forward_renew_settle_release_and_fence_with_the_lease_key_and_generation()
    {
        // given
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit();
        context
            .Store.RenewEnlistedAsync(resource, _Key, 7, FencingTestContext.Duration, AbortToken)
            .Returns(new LeaseRenewalResult(LeaseRenewalStatus.Renewed, DateTimeOffset.UnixEpoch));
        context.Store.SettleEnlistedAsync(resource, _Key, 7, AbortToken).Returns(LeaseSettlementStatus.Settled);
        context.Store.ReleaseEnlistedAsync(resource, _Key, 7, AbortToken).Returns(LeaseSettlementStatus.Released);
        context.Store.FenceEnlistedAsync(resource, _Key, 7, AbortToken).Returns(LeaseFenceStatus.Current);

        // when
        var renewal = await context.Feature.RenewAsync(unit, _Lease, FencingTestContext.Duration, AbortToken);
        var settlement = await context.Feature.SettleAsync(unit, _Lease, AbortToken);
        var release = await context.Feature.ReleaseAsync(unit, _Lease, AbortToken);
        await context.Feature.FenceAsync(unit, _Lease, AbortToken);

        // then
        renewal.Status.Should().Be(LeaseRenewalStatus.Renewed);
        settlement.Should().Be(LeaseSettlementStatus.Settled);
        release.Should().Be(LeaseSettlementStatus.Released);
        await context.Store.Received(1).FenceEnlistedAsync(resource, _Key, 7, AbortToken);
    }

    [Theory]
    [InlineData(LeaseFenceStatus.Stale)]
    [InlineData(LeaseFenceStatus.Expired)]
    [InlineData(LeaseFenceStatus.Settled)]
    [InlineData(LeaseFenceStatus.Released)]
    [InlineData(LeaseFenceStatus.Abandoned)]
    public async Task should_throw_stale_lease_when_the_fence_is_not_current(LeaseFenceStatus status)
    {
        // given
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit();
        context.Store.FenceEnlistedAsync(resource, _Key, 7, AbortToken).Returns(status);

        // when
        var act = async () => await context.Feature.FenceAsync(unit, _Lease, AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<StaleLeaseException>();
        thrown.Which.Lease.Should().Be(_Lease);
        thrown.Which.Reason.Should().Be(status);
    }

    [Fact]
    public async Task should_refuse_every_verb_on_a_unit_without_a_transaction_before_the_store()
    {
        // given
        var context = new FencingTestContext();
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns((IUnitOfWorkResource?)null);

        // when
        Func<Task>[] calls =
        [
            async () =>
                await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken),
            async () => await context.Feature.RenewAsync(unit, _Lease, FencingTestContext.Duration, AbortToken),
            async () => await context.Feature.SettleAsync(unit, _Lease, AbortToken),
            async () => await context.Feature.ReleaseAsync(unit, _Lease, AbortToken),
            async () => await context.Feature.FenceAsync(unit, _Lease, AbortToken),
        ];

        // then
        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("*relational resource*");
        }

        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Theory]
    [InlineData(UnitOfWorkState.Completed)]
    [InlineData(UnitOfWorkState.Failed)]
    public async Task should_refuse_a_unit_that_is_not_active_before_the_store(UnitOfWorkState state)
    {
        // given
        var context = new FencingTestContext();
        var (unit, _) = FencingTestContext.ActiveUnit(isOwned: false);
        unit.State.Returns(state);

        // when
        var act = async () =>
            await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{state}*fenced lease*");
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_refuse_a_unit_whose_transaction_completed_before_the_store()
    {
        // given
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit(isOwned: false);
        resource.IsTransactionCompleted.Returns(true);

        // when
        var act = async () => await context.Feature.SettleAsync(unit, _Lease, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already completed*");
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_validate_arguments_before_the_unit()
    {
        // given — the unit is dead, but the invalid argument must be reported first
        var context = new FencingTestContext();
        var (unit, _) = FencingTestContext.ActiveUnit();
        unit.State.Returns(UnitOfWorkState.Completed);

        // when
        var act = async () =>
            await context.Feature.GrantAsync(unit, " job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _AssertStoreUntouched(context);
    }

    [Fact]
    public async Task should_reject_a_grant_duration_outside_the_bounds_before_the_store()
    {
        // given
        var context = new FencingTestContext();
        var (unit, _) = FencingTestContext.ActiveUnit(isOwned: false);

        // when
        var tooShort = async () =>
            await context.Feature.GrantAsync(unit, "job", "order-1", TimeSpan.FromMilliseconds(10), AbortToken);
        var tooLong = async () => await context.Feature.RenewAsync(unit, _Lease, TimeSpan.FromDays(2), AbortToken);

        // then
        await tooShort.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await tooLong.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_refuse_a_null_unit()
    {
        var context = new FencingTestContext();

        var act = async () => await context.Feature.FenceAsync(null!, _Lease, AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
        _AssertStoreUntouched(context);
    }

    [Fact]
    public async Task should_not_prevent_retry_when_the_store_refuses_the_resource()
    {
        // given
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit(isOwned: false);
        context
            .Store.When(store => store.ValidateEnlistment(resource))
            .Do(_ => throw new InvalidOperationException("different database"));

        // when
        var act = async () =>
            await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("different database");
        unit.DidNotReceive().PreventRetry();
        await context.Store.DidNotReceiveWithAnyArgs().GrantEnlistedAsync(default!, default, default, AbortToken);
    }

    [Fact]
    public async Task should_prevent_retry_once_on_an_observed_resource_between_validation_and_the_write()
    {
        // given
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit(isOwned: false);

        // when
        await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken);

        // then
        unit.Received(1).PreventRetry();
        Received.InOrder(() =>
        {
            context.Store.ValidateEnlistment(resource);
            unit.PreventRetry();
            _ = context.Store.GrantEnlistedAsync(
                resource,
                Arg.Any<LeaseKey>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            );
        });
    }

    [Fact]
    public async Task should_prevent_retry_for_every_write_verb_on_an_observed_resource()
    {
        // given
        var context = new FencingTestContext();
        var (unit, _) = FencingTestContext.ActiveUnit(isOwned: false);

        // when
        await context.Feature.RenewAsync(unit, _Lease, FencingTestContext.Duration, AbortToken);
        await context.Feature.SettleAsync(unit, _Lease, AbortToken);
        await context.Feature.ReleaseAsync(unit, _Lease, AbortToken);

        // then
        unit.Received(3).PreventRetry();
    }

    [Fact]
    public async Task should_keep_an_owned_resource_replayable()
    {
        // given
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit(isOwned: true);

        // when
        await context.Feature.GrantAsync(unit, "job", "order-1", FencingTestContext.Duration, AbortToken);
        await context.Feature.SettleAsync(unit, _Lease, AbortToken);

        // then
        unit.DidNotReceive().PreventRetry();
        context.Store.Received(2).ValidateEnlistment(resource);
    }

    [Fact]
    public async Task should_never_prevent_retry_for_a_fence_even_on_an_observed_resource()
    {
        // given — the fence only reads, so a replay re-runs it harmlessly
        var context = new FencingTestContext();
        var (unit, resource) = FencingTestContext.ActiveUnit(isOwned: false);
        context.Store.FenceEnlistedAsync(resource, _Key, 7, AbortToken).Returns(LeaseFenceStatus.Current);

        // when
        await context.Feature.FenceAsync(unit, _Lease, AbortToken);

        // then
        unit.DidNotReceive().PreventRetry();
        context.Store.Received(1).ValidateEnlistment(resource);
    }

    [Fact]
    public async Task should_name_the_fenced_lease_operation_in_the_refusal()
    {
        var context = new FencingTestContext();
        var (unit, _) = FencingTestContext.ActiveUnit();
        unit.State.Returns(UnitOfWorkState.Completed);

        var act = async () => await context.Feature.FenceAsync(unit, _Lease, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*fenced lease*");
    }

    private static void _AssertStoreUntouched(FencingTestContext context)
    {
        context.Store.ReceivedCalls().Should().BeEmpty();
    }
}
