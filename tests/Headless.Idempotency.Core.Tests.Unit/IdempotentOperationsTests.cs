// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class IdempotentOperationsTests : TestBase
{
    [Fact]
    public async Task should_commit_an_admitted_admission_in_an_owned_unit_before_returning()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context
            .Store.LockOrInsertAsync(unit, RecordKey, Fingerprint, context.Retention, AbortToken)
            .Returns(Inserted());
        context
            .Store.AdmitAsync(unit, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, AbortToken)
            .Returns(Grant);

        // when
        var admission = await context.Operations.AdmitAsync(Key, Fingerprint, cancellationToken: AbortToken);

        // then - the lock and the lease grant share the owned unit, which commits once
        admission.Disposition.Should().Be(IdempotentDisposition.Admitted);
        await context
            .Store.Received(1)
            .AdmitAsync(unit, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, AbortToken);
        await unit.Received(1).CompleteAsync(CancellationToken.None);
        await unit.DidNotReceive().RollbackAsync();
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_roll_back_an_admission_that_did_not_admit()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context
            .Store.LockOrInsertAsync(unit, RecordKey, Fingerprint, context.Retention, AbortToken)
            .Returns(Pending(generation: 5, isLeaseLive: true));

        // when
        var admission = await context.Operations.AdmitAsync(Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.InFlight);
        await unit.Received(1).RollbackAsync();
        await unit.DidNotReceiveWithAnyArgs().CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_roll_back_and_rethrow_when_the_admission_fails()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context
            .Store.LockOrInsertAsync(unit, RecordKey, Fingerprint, context.Retention, AbortToken)
            .Returns(Pending(generation: 5, new IdempotencyFingerprint("v9", [1])));

        // when
        var act = async () => await context.Operations.AdmitAsync(Key, Fingerprint, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<NotSupportedException>();
        await unit.Received(1).RollbackAsync();
        await unit.DidNotReceiveWithAnyArgs().CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_commit_a_completion_in_an_owned_unit()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Operations.CompleteAsync(Admitted(), new byte[] { 1 }, "c/v1", cancellationToken: AbortToken);

        // then
        await context
            .Store.ReceivedWithAnyArgs(1)
            .CompleteAsync(default!, default, default, default, default!, default, AbortToken);
        await unit.Received(1).CompleteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task should_roll_back_and_throw_when_an_expired_attempt_completes()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: false));

        // when
        var act = async () =>
            await context.Operations.CompleteAsync(Admitted(), new byte[] { 1 }, "c/v1", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<StaleAdmissionException>();
        await unit.Received(1).RollbackAsync();
        await unit.DidNotReceiveWithAnyArgs().CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_commit_a_release_and_roll_back_a_refused_one()
    {
        // given
        var context = new IdempotencyTestContext();
        var (released, _) = ActiveUnit();
        var (refused, _) = ActiveUnit();
        context.Store.BeginOwnedUnitAsync(AbortToken).Returns(released, refused);
        context
            .Store.LockAsync(released, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));
        context
            .Store.LockAsync(refused, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: false));

        // when
        var first = await context.Operations.ReleaseAsync(Admitted(), AbortToken);
        var second = await context.Operations.ReleaseAsync(Admitted(), AbortToken);

        // then
        first.Should().Be(IdempotentLeaseStatus.Released);
        second.Should().Be(IdempotentLeaseStatus.Expired);
        await released.Received(1).CompleteAsync(CancellationToken.None);
        await refused.Received(1).RollbackAsync();
        await refused.DidNotReceiveWithAnyArgs().CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_renew_through_the_store_without_an_owned_unit()
    {
        // given
        var context = new IdempotencyTestContext();
        var renewed = new IdempotentLeaseRenewal(IdempotentLeaseStatus.Current, ExpiresAt);
        context.Store.RenewAsync(RecordKey, Generation, TimeSpan.FromMinutes(1), AbortToken).Returns(renewed);

        // when
        var result = await context.Operations.RenewAsync(Admitted(), TimeSpan.FromMinutes(1), AbortToken);

        // then
        result.Should().Be(renewed);
        await context.Store.DidNotReceiveWithAnyArgs().BeginOwnedUnitAsync(AbortToken);
    }

    [Fact]
    public async Task should_refuse_to_renew_an_admission_that_was_not_admitted()
    {
        // given
        var context = new IdempotencyTestContext();
        var replay = IdempotentAdmission.Replay(
            new IdempotencyKey("t1", Key),
            Fingerprint,
            new IdempotentResult([1], "c/v1")
        );

        // when
        var act = async () => await context.Operations.RenewAsync(replay, TimeSpan.FromMinutes(1), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Replay*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_renewal_duration_outside_the_bounds_before_the_store()
    {
        // given
        var context = new IdempotencyTestContext();

        // when
        var act = async () => await context.Operations.RenewAsync(Admitted(), TimeSpan.FromDays(2), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_peek_the_store_through_the_resolved_record_key()
    {
        // given
        var context = new IdempotencyTestContext();
        context.Store.PeekAsync(RecordKey, AbortToken).Returns(IdempotencyPeekStatus.Completed);

        // when
        var status = await context.Operations.PeekAsync(Key, AbortToken);

        // then
        status.Should().Be(IdempotencyPeekStatus.Completed);
        await context.Store.DidNotReceiveWithAnyArgs().BeginOwnedUnitAsync(AbortToken);
    }

    [Fact]
    public async Task should_refuse_to_peek_an_invalid_key()
    {
        // given
        var context = new IdempotencyTestContext();

        // when
        var act = async () => await context.Operations.PeekAsync(" bad", AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        await context.Store.DidNotReceiveWithAnyArgs().PeekAsync(default, AbortToken);
    }

    private IUnitOfWork _GivenOwnedUnit(IdempotencyTestContext context)
    {
        var (unit, _) = ActiveUnit(isOwned: true);
        context.Store.BeginOwnedUnitAsync(AbortToken).Returns(unit);

        return unit;
    }
}
