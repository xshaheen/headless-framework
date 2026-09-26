// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class UnitOfWorkIdempotencySettlementTests : TestBase
{
    private static readonly byte[] _Result = [4, 5, 6];
    private const string _Contract = "orders.receipt/v1";

    [Fact]
    public async Task should_lock_the_record_then_settle_the_lease_then_store_the_result()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        var admission = Admitted();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 7));
        context.Leases.SettleAsync(unit, Lease, AbortToken).Returns(LeaseSettlementStatus.Settled);

        // when
        await context.Feature.CompleteAsync(unit, admission, _Result, _Contract, cancellationToken: AbortToken);

        // then — the admission's own retention applies when the caller passes none
        Received.InOrder(() =>
        {
            _ = context.Store.LockAsync(resource, RecordKey, AbortToken);
            _ = context.Leases.SettleAsync(unit, Lease, AbortToken);
            _ = context.Store.CompleteAsync(
                resource,
                RecordKey,
                7,
                Arg.Is<ReadOnlyMemory<byte>>(m => _Result.SequenceEqual(m.ToArray())),
                _Contract,
                TimeSpan.FromDays(7),
                AbortToken
            );
        });
    }

    [Theory]
    [InlineData(LeaseSettlementStatus.Stale, LeaseFenceStatus.Stale)]
    [InlineData(LeaseSettlementStatus.Expired, LeaseFenceStatus.Expired)]
    [InlineData(LeaseSettlementStatus.Released, LeaseFenceStatus.Released)]
    [InlineData(LeaseSettlementStatus.Abandoned, LeaseFenceStatus.Abandoned)]
    public async Task should_refuse_completion_and_store_nothing_when_the_lease_does_not_settle(
        LeaseSettlementStatus settlement,
        LeaseFenceStatus reason
    )
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 7));
        context.Leases.SettleAsync(unit, Lease, AbortToken).Returns(settlement);

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(unit, Admitted(), _Result, _Contract, cancellationToken: AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<StaleLeaseException>();
        thrown.Which.Lease.Should().Be(Lease);
        thrown.Which.Reason.Should().Be(reason);
        await context
            .Store.DidNotReceiveWithAnyArgs()
            .CompleteAsync(default!, default, default, default, default!, default, AbortToken);
    }

    [Theory]
    [InlineData(8L)]
    [InlineData(null)]
    public async Task should_refuse_completion_before_the_lease_when_the_record_names_another_attempt(long? generation)
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation));

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(unit, Admitted(), _Result, _Contract, cancellationToken: AbortToken);

        // then
        (await act.Should().ThrowAsync<StaleLeaseException>())
            .Which.Reason.Should()
            .Be(LeaseFenceStatus.Stale);
        context.Leases.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_completion_when_the_record_is_gone()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns((IdempotencyRecordState?)null);

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(unit, Admitted(), _Result, _Contract, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<StaleLeaseException>();
        context.Leases.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_to_complete_an_admission_that_was_not_admitted()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        var inFlight = IdempotentAdmission.InFlight(new IdempotencyKey("t1", Key), Fingerprint, ExpiresAt);

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(unit, inFlight, _Result, _Contract, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*InFlight*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_release_the_lease_then_free_the_record()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 7));
        context.Leases.ReleaseAsync(unit, Lease, AbortToken).Returns(LeaseSettlementStatus.Released);

        // when
        var status = await context.Feature.ReleaseAsync(unit, Admitted(), AbortToken);

        // then
        status.Should().Be(LeaseSettlementStatus.Released);
        Received.InOrder(() =>
        {
            _ = context.Store.LockAsync(resource, RecordKey, AbortToken);
            _ = context.Leases.ReleaseAsync(unit, Lease, AbortToken);
            _ = context.Store.ReleaseAsync(resource, RecordKey, 7, TimeSpan.FromDays(7), AbortToken);
        });
    }

    [Theory]
    [InlineData(LeaseSettlementStatus.Stale)]
    [InlineData(LeaseSettlementStatus.Expired)]
    [InlineData(LeaseSettlementStatus.Settled)]
    public async Task should_leave_the_record_when_the_lease_release_is_refused(LeaseSettlementStatus refusal)
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 7));
        context.Leases.ReleaseAsync(unit, Lease, AbortToken).Returns(refusal);

        // when
        var status = await context.Feature.ReleaseAsync(unit, Admitted(), AbortToken);

        // then
        status.Should().Be(refusal);
        await context.Store.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default, default, default, AbortToken);
    }

    [Fact]
    public async Task should_report_a_release_as_stale_when_the_record_names_another_attempt()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 8));

        // when
        var status = await context.Feature.ReleaseAsync(unit, Admitted(), AbortToken);

        // then
        status.Should().Be(LeaseSettlementStatus.Stale);
        context.Leases.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_fence_by_locking_the_record_before_the_lease_without_preventing_retry()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit(isOwned: false);
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 7));

        // when
        await context.Feature.FenceAsync(unit, Admitted(), AbortToken);

        // then
        Received.InOrder(() =>
        {
            _ = context.Store.LockAsync(resource, RecordKey, AbortToken);
            _ = context.Leases.FenceAsync(unit, Lease, AbortToken);
        });
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_refuse_the_fence_before_the_lease_when_the_record_names_another_attempt()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 8));

        // when
        var act = async () => await context.Feature.FenceAsync(unit, Admitted(), AbortToken);

        // then
        await act.Should().ThrowAsync<StaleLeaseException>();
        context.Leases.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_let_the_lease_fence_refusal_propagate()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        context.Store.LockAsync(resource, RecordKey, AbortToken).Returns(Pending(generation: 7));
        context
            .Leases.FenceAsync(unit, Lease, AbortToken)
            .Returns(ValueTask.FromException(new StaleLeaseException(Lease, LeaseFenceStatus.Expired)));

        // when
        var act = async () => await context.Feature.FenceAsync(unit, Admitted(), AbortToken);

        // then
        (await act.Should().ThrowAsync<StaleLeaseException>())
            .Which.Reason.Should()
            .Be(LeaseFenceStatus.Expired);
    }
}
