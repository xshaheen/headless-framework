// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class UnitOfWorkIdempotencyRecoveryPointTests : TestBase
{
    private const string _Point = "payment-captured";
    private const string _Contract = "orders.payment/v1";
    private static readonly byte[] _State = [1, 2, 3];
    private static readonly IdempotentRecoveryPoint _Recorded = new(_Point, _State, _Contract);

    #region Set

    [Fact]
    public async Task should_lock_the_record_then_write_the_point_while_the_attempt_owns_it()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Feature.SetRecoveryPointAsync(unit, Admitted(), _Point, _State, _Contract, AbortToken);

        // then
        Received.InOrder(() =>
        {
            context.Store.ValidateEnlistment(unit);
            _ = context.Store.LockAsync(unit, RecordKey, AbortToken);
            _ = context.Store.SetRecoveryPointAsync(
                unit,
                RecordKey,
                Generation,
                _Point,
                Arg.Is<ReadOnlyMemory<byte>>(m => _State.SequenceEqual(m.ToArray())),
                _Contract,
                AbortToken
            );
        });
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_make_an_observed_unit_non_retryable_when_it_writes_a_point()
    {
        // given - the unit belongs to someone else's commit edge, whose replay would not re-run this write
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit(isOwned: false);
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Feature.SetRecoveryPointAsync(unit, Admitted(), _Point, _State, _Contract, AbortToken);

        // then
        unit.Received(1).PreventRetry();
    }

    public static TheoryData<IdempotencyRecordState?, IdempotentLeaseStatus> NotOwned =>
        new()
        {
            { Pending(generation: Generation, isLeaseLive: false), IdempotentLeaseStatus.Expired },
            { Pending(generation: 8, isLeaseLive: true), IdempotentLeaseStatus.Stale },
            { Pending(generation: null), IdempotentLeaseStatus.Released },
            { Completed(new IdempotentResult([9], "r/v1")), IdempotentLeaseStatus.Completed },
            { null, IdempotentLeaseStatus.Stale },
        };

    [Theory]
    [MemberData(nameof(NotOwned))]
    public async Task should_refuse_the_point_and_write_nothing_when_the_attempt_no_longer_owns_the_key(
        IdempotencyRecordState? record,
        IdempotentLeaseStatus reason
    )
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context.Store.LockAsync(unit, RecordKey, AbortToken).Returns(record);

        // when
        var act = async () =>
            await context.Feature.SetRecoveryPointAsync(unit, Admitted(), _Point, _State, _Contract, AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<StaleAdmissionException>();
        thrown.Which.Generation.Should().Be(Generation);
        thrown.Which.Reason.Should().Be(reason);
        await context
            .Store.DidNotReceiveWithAnyArgs()
            .SetRecoveryPointAsync(default!, default, default, default!, default, default!, AbortToken);
    }

    public static TheoryData<string, int, string> InvalidPoints =>
        new()
        {
            { "", 1, _Contract },
            { "   ", 1, _Contract },
            { new string('p', IdempotencyFieldLimits.RecoveryPointMaxLength + 1), 1, _Contract },
            { _Point, IdempotencyFieldLimits.RecoveryStateMaxLength + 1, _Contract },
            { _Point, 1, "" },
            { _Point, 1, " padded" },
            { _Point, 1, new string('c', IdempotencyFieldLimits.ContractMaxLength + 1) },
        };

    [Theory]
    [MemberData(nameof(InvalidPoints))]
    public async Task should_refuse_an_invalid_or_oversized_point_before_any_store_call(
        string point,
        int stateLength,
        string contract
    )
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit(isOwned: false);

        // when
        var act = async () =>
            await context.Feature.SetRecoveryPointAsync(
                unit,
                Admitted(),
                point,
                new byte[stateLength],
                contract,
                AbortToken
            );

        // then - refused before the unit is judged, so it stays retryable
        await act.Should().ThrowAsync<ArgumentException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_accept_a_state_at_the_size_limit_and_an_empty_state()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Feature.SetRecoveryPointAsync(
            unit,
            Admitted(),
            _Point,
            new byte[IdempotencyFieldLimits.RecoveryStateMaxLength],
            _Contract,
            AbortToken
        );
        await context.Feature.SetRecoveryPointAsync(
            unit,
            Admitted(),
            "done",
            ReadOnlyMemory<byte>.Empty,
            _Contract,
            AbortToken
        );

        // then
        await context
            .Store.ReceivedWithAnyArgs(2)
            .SetRecoveryPointAsync(default!, default, default, default!, default, default!, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_point_for_an_admission_that_was_not_admitted()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        var inFlight = IdempotentAdmission.InFlight(new IdempotencyKey("t1", Key), Fingerprint, 5, ExpiresAt);

        // when
        var act = async () =>
            await context.Feature.SetRecoveryPointAsync(unit, inFlight, _Point, _State, _Contract, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*InFlight*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    #endregion

    #region Admission

    [Fact]
    public async Task should_hand_a_takeover_the_crashed_attempt_recovery_point_and_keep_it_on_the_record()
    {
        // given - an earlier attempt recorded a point and then let its lease expire
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        _GivenRecord(context, unit, Pending(generation: 3, isLeaseLive: false) with { RecoveryPoint = _Recorded });
        _GivenGrant(context, unit);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.IsTakeover.Should().BeTrue();
        admission.RecoveryPoint.Should().BeSameAs(_Recorded);
        await context
            .Store.Received(1)
            .AdmitAsync(unit, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, true, AbortToken);
    }

    [Fact]
    public async Task should_hand_the_next_attempt_the_recovery_point_a_released_attempt_kept()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        _GivenRecord(context, unit, Pending(generation: null) with { RecoveryPoint = _Recorded });
        _GivenGrant(context, unit);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.IsTakeover.Should().BeFalse("a release is not a crash");
        admission.RecoveryPoint.Should().BeSameAs(_Recorded);
        await context
            .Store.Received(1)
            .AdmitAsync(unit, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, true, AbortToken);
    }

    [Fact]
    public async Task should_drop_the_recovery_point_when_the_record_is_reset_past_its_retention()
    {
        // given - the record's retention elapsed, so the next admission is a new operation, not a resumption
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        _GivenRecord(
            context,
            unit,
            Pending(generation: 3, isRetentionElapsed: true) with
            {
                RecoveryPoint = _Recorded,
            }
        );
        _GivenGrant(context, unit);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.RecoveryPoint.Should().BeNull();
        await context
            .Store.Received(1)
            .AdmitAsync(unit, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, false, AbortToken);
    }

    [Fact]
    public async Task should_not_hand_a_first_admission_a_recovery_point()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        _GivenRecord(context, unit, Inserted());
        _GivenGrant(context, unit);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.RecoveryPoint.Should().BeNull();
        await context
            .Store.Received(1)
            .AdmitAsync(unit, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, false, AbortToken);
    }

    #endregion

    #region Autonomous

    [Fact]
    public async Task should_commit_an_autonomous_point_in_an_owned_unit()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Operations.SetRecoveryPointAsync(Admitted(), _Point, _State, _Contract, AbortToken);

        // then - the step already happened, so a late cancel must not discard its record
        await context
            .Store.ReceivedWithAnyArgs(1)
            .SetRecoveryPointAsync(default!, default, default, default!, default, default!, AbortToken);
        await unit.Received(1).CompleteAsync(CancellationToken.None);
        await unit.DidNotReceive().RollbackAsync();
    }

    [Fact]
    public async Task should_roll_back_and_rethrow_an_autonomous_point_the_store_refuses()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = _GivenOwnedUnit(context);
        context.Store.LockAsync(unit, RecordKey, AbortToken).Returns(Pending(generation: 8, isLeaseLive: true));

        // when
        var act = async () =>
            await context.Operations.SetRecoveryPointAsync(Admitted(), _Point, _State, _Contract, AbortToken);

        // then
        await act.Should().ThrowAsync<StaleAdmissionException>();
        await unit.Received(1).RollbackAsync();
        await unit.DidNotReceiveWithAnyArgs().CompleteAsync(AbortToken);
    }

    [Fact]
    public async Task should_round_trip_a_typed_recovery_state_through_source_generated_metadata()
    {
        // given
        var operations = Substitute.For<IIdempotentOperations>();
        var admission = Admitted();
        var receipt = new Receipt("R-7", 5m);
        ReadOnlyMemory<byte> written = default;
        operations
            .SetRecoveryPointAsync(
                admission,
                "done",
                Arg.Do<ReadOnlyMemory<byte>>(bytes => written = bytes.ToArray()),
                "receipt/v1",
                AbortToken
            )
            .Returns(ValueTask.CompletedTask);

        // when
        await operations.SetRecoveryPointAsync(
            admission,
            "done",
            receipt,
            ReceiptJsonContext.Default.Receipt,
            "receipt/v1",
            AbortToken
        );
        var resumed = new IdempotentRecoveryPoint("done", written.Span, "receipt/v1").Deserialize(
            ReceiptJsonContext.Default.Receipt
        );

        // then
        resumed.Should().Be(receipt);
    }

    #endregion

    private static void _GivenRecord(IdempotencyTestContext context, IUnitOfWork unit, IdempotencyRecordState state)
    {
        context.Store.LockOrInsertAsync(unit, RecordKey, Fingerprint, context.Retention, AbortToken).Returns(state);
    }

    private static void _GivenGrant(IdempotencyTestContext context, IUnitOfWork unit)
    {
        context
            .Store.AdmitAsync(
                unit,
                RecordKey,
                Fingerprint,
                context.LeaseDuration,
                context.Retention,
                Arg.Any<bool>(),
                AbortToken
            )
            .Returns(Grant);
    }

    private static IUnitOfWork _GivenOwnedUnit(IdempotencyTestContext context)
    {
        var (unit, _) = ActiveUnit(isOwned: true);
        context.Store.BeginOwnedUnitAsync(AbortToken).Returns(unit);

        return unit;
    }
}
