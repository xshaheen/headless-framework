// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class UnitOfWorkIdempotencyAdmissionTests : TestBase
{
    private static readonly IdempotentResult _Stored = new([1, 2, 3], "orders.receipt/v1");

    [Fact]
    public async Task should_admit_a_new_key_and_draw_the_generation_after_locking_the_record()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Inserted());
        _GivenGrant(context, resource);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Admitted);
        admission.Generation.Should().Be(Generation);
        admission.LeaseExpiresAt.Should().Be(ExpiresAt);
        admission.IsTakeover.Should().BeFalse();
        admission.Key.Should().Be(new IdempotencyKey("t1", Key));
        admission.Retention.Should().Be(context.Retention);

        Received.InOrder(() =>
        {
            _ = context.Store.LockOrInsertAsync(resource, RecordKey, Fingerprint, context.Retention, AbortToken);
            _ = context.Store.AdmitAsync(
                resource,
                RecordKey,
                Fingerprint,
                context.LeaseDuration,
                context.Retention,
                AbortToken
            );
        });
    }

    [Fact]
    public async Task should_conflict_with_the_stored_fingerprint_when_the_key_is_reused_for_another_request()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Completed(_Stored, OtherFingerprint));

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Conflict);
        admission.StoredFingerprint.Should().Be(OtherFingerprint);
        admission.StoredContract.Should().BeNull();
        admission.Result.Should().BeNull();
        _AssertNoRecordWrite(context);
    }

    [Fact]
    public async Task should_conflict_on_a_different_fingerprint_while_an_attempt_is_pending()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Pending(generation: 5, OtherFingerprint));

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Conflict);
        admission.StoredFingerprint.Should().Be(OtherFingerprint);
        _AssertNoRecordWrite(context);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("orders.receipt/v1")]
    public async Task should_replay_the_stored_result_when_completed_within_retention(string? expectedContract)
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Completed(_Stored));

        // when
        var admission = await context.Feature.AdmitAsync(
            unit,
            Key,
            Fingerprint,
            expectedContract,
            cancellationToken: AbortToken
        );

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Replay);
        admission.Result.Should().BeSameAs(_Stored);
        admission.Result!.Payload.ToArray().Should().Equal(1, 2, 3);
        admission.Generation.Should().BeNull();
        _AssertNoRecordWrite(context);
    }

    [Fact]
    public async Task should_conflict_when_the_stored_contract_differs_from_the_expected_one()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Completed(_Stored));

        // when
        var admission = await context.Feature.AdmitAsync(
            unit,
            Key,
            Fingerprint,
            "orders.receipt/v2",
            cancellationToken: AbortToken
        );

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Conflict);
        admission.StoredContract.Should().Be("orders.receipt/v1");
        admission.Result.Should().BeNull();
        _AssertNoRecordWrite(context);
    }

    [Fact]
    public async Task should_be_in_flight_when_a_live_attempt_holds_the_lease()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Pending(generation: 5, isLeaseLive: true));

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.InFlight);
        admission.LeaseExpiresAt.Should().Be(ExpiresAt);
        admission.Generation.Should().Be(5);
        admission.IsAdmitted.Should().BeFalse();
        _AssertNoRecordWrite(context);
    }

    [Fact]
    public async Task should_stay_in_flight_past_retention_while_the_holder_lease_is_live()
    {
        // given - a lease renewed past the record's retention still owns the key
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Pending(generation: 5, isRetentionElapsed: true, isLeaseLive: true));

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.InFlight);
        _AssertNoRecordWrite(context);
    }

    [Fact]
    public async Task should_admit_as_a_takeover_when_the_pending_attempt_lease_expired()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Pending(generation: 5, isLeaseLive: false));
        _GivenGrant(context, resource);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Admitted);
        admission.IsTakeover.Should().BeTrue();
        admission.Generation.Should().Be(Generation);
        await context
            .Store.Received(1)
            .AdmitAsync(resource, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, AbortToken);
    }

    [Fact]
    public async Task should_not_report_a_takeover_after_the_previous_attempt_released()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Pending(generation: null));
        _GivenGrant(context, resource);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Admitted);
        admission.IsTakeover.Should().BeFalse();
    }

    [Fact]
    public async Task should_reset_a_record_past_retention_in_place_and_admit_even_with_another_fingerprint()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Completed(_Stored, OtherFingerprint, isRetentionElapsed: true));
        _GivenGrant(context, resource);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Disposition.Should().Be(IdempotentDisposition.Admitted);
        admission.IsTakeover.Should().BeFalse();
        await context
            .Store.Received(1)
            .AdmitAsync(resource, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_stored_fingerprint_from_an_unknown_algorithm_without_recomputing()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        _GivenRecord(context, resource, Pending(generation: 5, new IdempotencyFingerprint("v9", [9, 9])));

        // when
        var act = async () => await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*'v9'*");
        _AssertNoRecordWrite(context);
    }

    [Fact]
    public async Task should_refuse_a_caller_fingerprint_from_an_unknown_algorithm_before_the_store()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();

        // when
        var act = async () =>
            await context.Feature.AdmitAsync(
                unit,
                Key,
                new IdempotencyFingerprint("v9", [9]),
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*'v9'*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" order-1")]
    [InlineData("order-1 ")]
    public async Task should_refuse_an_invalid_key_before_the_store(string key)
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();

        // when
        var act = async () => await context.Feature.AdmitAsync(unit, key, Fingerprint, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_key_longer_than_the_column_before_the_store()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();
        var key = new string('k', IdempotencyFieldLimits.KeyMaxLength + 1);

        // when
        var act = async () => await context.Feature.AdmitAsync(unit, key, Fingerprint, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_key_the_host_scope_as_the_empty_tenant()
    {
        // given
        var context = new IdempotencyTestContext();
        context.Tenant.Id = null;
        var (unit, resource) = ActiveUnit();
        var hostKey = new IdempotencyRecordKey(string.Empty, Key);
        context
            .Store.LockOrInsertAsync(resource, hostKey, Fingerprint, context.Retention, AbortToken)
            .Returns(Inserted());
        context
            .Store.AdmitAsync(resource, hostKey, Fingerprint, context.LeaseDuration, context.Retention, AbortToken)
            .Returns(Grant);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        admission.Key.Should().Be(new IdempotencyKey(null, Key));
        admission.Generation.Should().Be(Generation);
    }

    [Fact]
    public async Task should_use_the_caller_durations_over_the_defaults()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit();
        var lease = TimeSpan.FromSeconds(45);
        var retention = TimeSpan.FromDays(7);
        context.Store.LockOrInsertAsync(resource, RecordKey, Fingerprint, retention, AbortToken).Returns(Inserted());
        context.Store.AdmitAsync(resource, RecordKey, Fingerprint, lease, retention, AbortToken).Returns(Grant);

        // when
        var admission = await context.Feature.AdmitAsync(unit, Key, Fingerprint, null, lease, retention, AbortToken);

        // then
        admission.Retention.Should().Be(retention);
        await context.Store.Received(1).AdmitAsync(resource, RecordKey, Fingerprint, lease, retention, AbortToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task should_refuse_a_lease_duration_outside_the_bounds_before_the_store(int milliseconds)
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, _) = ActiveUnit();

        // when
        var act = async () =>
            await context.Feature.AdmitAsync(
                unit,
                Key,
                Fingerprint,
                leaseDuration: TimeSpan.FromMilliseconds(milliseconds),
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_prevent_retry_on_an_observed_unit_but_not_on_an_owned_one()
    {
        // given
        var context = new IdempotencyTestContext();
        var (observed, observedResource) = ActiveUnit(isOwned: false);
        var (owned, ownedResource) = ActiveUnit(isOwned: true);
        _GivenRecord(context, observedResource, Completed(_Stored));
        _GivenRecord(context, ownedResource, Completed(_Stored));

        // when
        await context.Feature.AdmitAsync(observed, Key, Fingerprint, cancellationToken: AbortToken);
        await context.Feature.AdmitAsync(owned, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        observed.Received(1).PreventRetry();
        owned.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_validate_the_enlistment_before_touching_the_record()
    {
        // given
        var context = new IdempotencyTestContext();
        var (unit, resource) = ActiveUnit(isOwned: false);
        context
            .Store.When(x => x.ValidateEnlistment(resource))
            .Do(_ => throw new InvalidOperationException("other database"));

        // when
        var act = async () => await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("other database");
        unit.DidNotReceive().PreventRetry();
        await context
            .Store.DidNotReceiveWithAnyArgs()
            .LockOrInsertAsync(default!, default, default!, default, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_unit_without_a_transaction_before_the_store()
    {
        // given
        var context = new IdempotencyTestContext();
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns((IUnitOfWorkResource?)null);

        // when
        var act = async () => await context.Feature.AdmitAsync(unit, Key, Fingerprint, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*relational resource*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    private void _GivenRecord(
        IdempotencyTestContext context,
        IRelationalUnitOfWorkResource resource,
        IdempotencyRecordState state
    )
    {
        context.Store.LockOrInsertAsync(resource, RecordKey, Fingerprint, context.Retention, AbortToken).Returns(state);
    }

    private void _GivenGrant(IdempotencyTestContext context, IRelationalUnitOfWorkResource resource)
    {
        context
            .Store.AdmitAsync(resource, RecordKey, Fingerprint, context.LeaseDuration, context.Retention, AbortToken)
            .Returns(Grant);
    }

    private static void _AssertNoRecordWrite(IdempotencyTestContext context)
    {
        context
            .Store.ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Should()
            .NotContain([
                nameof(IIdempotencyRecordStore.AdmitAsync),
                nameof(IIdempotencyRecordStore.CompleteAsync),
                nameof(IIdempotencyRecordStore.ReleaseAsync),
            ]);
    }
}
