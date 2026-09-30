// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class UnitOfWorkIdempotencySettlementTests : TestBase
{
    private static readonly byte[] _Result = [4, 5, 6];
    private static readonly IdempotentResult _Stored = new(_Result, "orders.receipt/v1");
    private const string _Contract = "orders.receipt/v1";

    [Fact]
    public async Task should_lock_the_record_then_store_the_result_while_the_attempt_owns_it()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        var admission = Admitted();
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Feature.CompleteAsync(unit, admission, _Result, _Contract, cancellationToken: AbortToken);

        // then - the admission's own retention applies when the caller passes none
        Received.InOrder(() =>
        {
            _ = context.Store.LockAsync(unit, RecordKey, AbortToken);
            _ = context.Store.CompleteAsync(
                unit,
                RecordKey,
                Generation,
                Arg.Is<ReadOnlyMemory<byte>>(m => _Result.SequenceEqual(m.ToArray())),
                _Contract,
                TimeSpan.FromDays(7),
                AbortToken
            );
        });
    }

    public static TheoryData<NotOwnedRecord, IdempotentLeaseStatus> NotOwned =>
        new()
        {
            { NotOwnedRecord.ExpiredLease, IdempotentLeaseStatus.Expired },
            { NotOwnedRecord.NewerGeneration, IdempotentLeaseStatus.Stale },
            { NotOwnedRecord.Released, IdempotentLeaseStatus.Released },
            { NotOwnedRecord.Completed, IdempotentLeaseStatus.Completed },
            { NotOwnedRecord.Missing, IdempotentLeaseStatus.Stale },
        };

    private static IdempotencyRecordState? _Record(NotOwnedRecord kind)
    {
        return kind switch
        {
            NotOwnedRecord.ExpiredLease => Pending(generation: Generation, isLeaseLive: false),
            NotOwnedRecord.NewerGeneration => Pending(generation: 8, isLeaseLive: true),
            NotOwnedRecord.Released => Pending(generation: null),
            NotOwnedRecord.Completed => Completed(_Stored),
            NotOwnedRecord.Missing => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    [Theory]
    [MemberData(nameof(NotOwned))]
    public async Task should_refuse_completion_and_store_nothing_when_the_attempt_no_longer_owns_the_key(
        NotOwnedRecord kind,
        IdempotentLeaseStatus reason
    )
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context.Store.LockAsync(unit, RecordKey, AbortToken).Returns(_Record(kind));

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(unit, Admitted(), _Result, _Contract, cancellationToken: AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<StaleAdmissionException>();
        thrown.Which.Generation.Should().Be(Generation);
        thrown.Which.Key.Should().Be(new IdempotencyKey("t1", Key));
        thrown.Which.Reason.Should().Be(reason);
        await context
            .Store.DidNotReceiveWithAnyArgs()
            .CompleteAsync(default!, default, default, default, default!, default, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_second_completion_by_the_same_attempt()
    {
        // given - the record already completed under this admission's generation
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context.Store.LockAsync(unit, RecordKey, AbortToken).Returns(Completed(_Stored));

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(
                unit,
                Admitted(),
                "\t"u8.ToArray(),
                _Contract,
                cancellationToken: AbortToken
            );

        // then
        (await act.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Completed);
        await context
            .Store.DidNotReceiveWithAnyArgs()
            .CompleteAsync(default!, default, default, default, default!, default, AbortToken);
    }

    [Fact]
    public async Task should_refuse_to_complete_an_admission_that_was_not_admitted()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        var inFlight = IdempotentAdmission.InFlight(new IdempotencyKey("t1", Key), Fingerprint, 5, ExpiresAt);

        // when
        var act = async () =>
            await context.Feature.CompleteAsync(unit, inFlight, _Result, _Contract, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*InFlight*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_lock_the_record_then_free_it_on_release()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        var status = await context.Feature.ReleaseAsync(unit, Admitted(), AbortToken);

        // then
        status.Should().Be(IdempotentLeaseStatus.Released);
        Received.InOrder(() =>
        {
            _ = context.Store.LockAsync(unit, RecordKey, AbortToken);
            _ = context.Store.ReleaseAsync(unit, RecordKey, Generation, TimeSpan.FromDays(7), AbortToken);
        });
    }

    public static TheoryData<NotOwnedRecord, IdempotentLeaseStatus> ReleaseRefusals =>
        new()
        {
            { NotOwnedRecord.ExpiredLease, IdempotentLeaseStatus.Expired },
            { NotOwnedRecord.NewerGeneration, IdempotentLeaseStatus.Stale },
            { NotOwnedRecord.Completed, IdempotentLeaseStatus.Completed },
            { NotOwnedRecord.Missing, IdempotentLeaseStatus.Stale },
            // Already released: success again, and nothing to write.
            { NotOwnedRecord.Released, IdempotentLeaseStatus.Released },
        };

    [Theory]
    [MemberData(nameof(ReleaseRefusals))]
    public async Task should_leave_the_record_when_the_attempt_no_longer_owns_it(
        NotOwnedRecord kind,
        IdempotentLeaseStatus expected
    )
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context.Store.LockAsync(unit, RecordKey, AbortToken).Returns(_Record(kind));

        // when
        var status = await context.Feature.ReleaseAsync(unit, Admitted(), AbortToken);

        // then
        status.Should().Be(expected);
        await context.Store.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default, default, default, AbortToken);
    }

    [Fact]
    public async Task should_fence_by_locking_the_record_without_preventing_retry()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit(isOwned: false);
        context
            .Store.LockAsync(unit, RecordKey, AbortToken)
            .Returns(Pending(generation: Generation, isLeaseLive: true));

        // when
        await context.Feature.FenceAsync(unit, Admitted(), AbortToken);

        // then
        await context.Store.Received(1).LockAsync(unit, RecordKey, AbortToken);
        unit.DidNotReceive().PreventRetry();
    }

    [Theory]
    [MemberData(nameof(NotOwned))]
    public async Task should_refuse_the_fence_when_the_attempt_no_longer_owns_the_key(
        NotOwnedRecord kind,
        IdempotentLeaseStatus reason
    )
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        context.Store.LockAsync(unit, RecordKey, AbortToken).Returns(_Record(kind));

        // when
        var act = async () => await context.Feature.FenceAsync(unit, Admitted(), AbortToken);

        // then
        (await act.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(reason);
    }
}
