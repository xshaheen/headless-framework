// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// The provider-neutral contract of durable idempotency, run against a real database that holds the record table, whose
/// rows carry their own lease. A provider leaf derives from it, supplies its <see cref="IIdempotencyFixture" />, and
/// overrides every test with <c>[Fact]</c>.
/// </summary>
/// <remarks>
/// Each test uses its own freshly named keys, so tests never share a row. Lease expiry and the passing of retention
/// are produced by moving a row's instants into the database's past rather than by waiting.
/// </remarks>
#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public abstract class IdempotencyConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IIdempotencyFixture
{
    /// <summary>A lease duration no test outlives, so an admitted attempt stays live unless a test ages its lease.</summary>
    protected static readonly TimeSpan LongLease = TimeSpan.FromMinutes(5);

    /// <summary>The retention most tests admit with.</summary>
    protected static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    protected static readonly IdempotencyFingerprint Fingerprint = IdempotencyFingerprint.Compute("request-a");

    protected static readonly IdempotencyFingerprint OtherFingerprint = IdempotencyFingerprint.Compute("request-b");

    protected const string Contract = "test-result.v1";

    protected TFixture Fixture { get; } = fixture;

    #region Admission races

    public virtual async Task should_admit_exactly_one_of_many_parallel_autonomous_admissions()
    {
        await using var hostA = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        for (var round = 0; round < 3; round++)
        {
            var key = CreateKey();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var racers = Enumerable
                .Range(0, 16)
                .Select(i =>
                    Task.Run(
                        async () =>
                        {
                            await start.Task;

                            return await (i % 2 == 0 ? hostA : hostB).Operations.AdmitAsync(
                                key,
                                Fingerprint,
                                leaseDuration: LongLease,
                                retention: Retention,
                                cancellationToken: AbortToken
                            );
                        },
                        AbortToken
                    )
                )
                .ToList();

            start.SetResult();
            var results = await Task.WhenAll(racers);

            var admitted = results.Where(static r => r.IsAdmitted).ToList();
            admitted.Should().ContainSingle($"round {round} has one winner");
            admitted[0].IsTakeover.Should().BeFalse();
            results
                .Where(static r => !r.IsAdmitted)
                .Should()
                .AllSatisfy(r =>
                    r.Disposition.Should().BeOneOf(IdempotentDisposition.InFlight, IdempotentDisposition.Replay)
                );

            var stored = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
            stored!.Status.Should().Be(IdempotencyRecordStatus.Pending);
            stored.Generation.Should().Be(admitted[0].Generation);
            stored.LeaseExpiresAt.Should().NotBeNull();
        }
    }

    public virtual async Task should_serialize_parallel_enlisted_admissions_and_replay_the_winner()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each racer admits inside its own unit and, when admitted, completes and commits in that same unit. Every
        // other racer waits on the record the winner inserted or locked until the winner commits, then replays.
        var racers = Enumerable
            .Range(0, 8)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await start.Task;
                        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
                        var admission = await unit.Unit.Idempotency.AdmitAsync(
                            key,
                            Fingerprint,
                            leaseDuration: LongLease,
                            retention: Retention,
                            cancellationToken: AbortToken
                        );

                        if (admission.IsAdmitted)
                        {
                            await unit.Unit.Idempotency.CompleteAsync(
                                admission,
                                Payload("winner"),
                                Contract,
                                cancellationToken: AbortToken
                            );
                        }

                        await unit.CommitAsync(AbortToken);

                        return admission;
                    },
                    AbortToken
                )
            )
            .ToList();

        start.SetResult();
        var results = await Task.WhenAll(racers);

        results.Where(static r => r.IsAdmitted).Should().ContainSingle();
        results
            .Where(static r => !r.IsAdmitted)
            .Should()
            .HaveCount(7)
            .And.AllSatisfy(r =>
            {
                r.Disposition.Should().Be(IdempotentDisposition.Replay);
                Text(r.Result!.Payload).Should().Be("winner");
            });
    }

    public virtual async Task should_admit_a_blocked_admission_when_the_enlisted_winner_rolls_back()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var winner = await Fixture.BeginUnitAsync(host, AbortToken);

        var won = await winner.Unit.Idempotency.AdmitAsync(
            key,
            Fingerprint,
            leaseDuration: LongLease,
            retention: Retention,
            cancellationToken: AbortToken
        );
        won.IsAdmitted.Should().BeTrue();

        var pending = host
            .Operations.AdmitAsync(
                key,
                Fingerprint,
                leaseDuration: LongLease,
                retention: Retention,
                cancellationToken: AbortToken
            )
            .AsTask();
        var first = await Task.WhenAny(
            pending,
            Task.Delay(IdempotencyFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().NotBeSameAs(pending, "the admission waits on the record the open unit inserted");

        await winner.RollbackAsync();
        var next = await pending.WaitAsync(IdempotencyFixtureExtensions.ReleaseTimeout, AbortToken);

        next.IsAdmitted.Should().BeTrue("the rolled-back winner left no record and no lease");
        next.IsTakeover.Should().BeFalse();
        await host.Operations.CompleteAsync(next, Payload("after-rollback"), Contract, cancellationToken: AbortToken);
    }

    #endregion

    #region Outcomes

    public virtual async Task should_return_conflict_for_a_different_fingerprint_and_keep_the_stored_result()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var admitted = await AdmitAsync(host, key);
        await host.Operations.CompleteAsync(admitted, Payload("stored"), Contract, cancellationToken: AbortToken);
        var before = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);

        var conflict = await host.Operations.AdmitAsync(
            key,
            OtherFingerprint,
            retention: Retention,
            cancellationToken: AbortToken
        );

        conflict.Disposition.Should().Be(IdempotentDisposition.Conflict);
        conflict.StoredFingerprint.Should().Be(Fingerprint);
        conflict.Result.Should().BeNull();

        var contractConflict = await host.Operations.AdmitAsync(
            key,
            Fingerprint,
            expectedContract: "other-result.v1",
            retention: Retention,
            cancellationToken: AbortToken
        );

        contractConflict.Disposition.Should().Be(IdempotentDisposition.Conflict);
        contractConflict.StoredContract.Should().Be(Contract);

        var after = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
        after!.Status.Should().Be(IdempotencyRecordStatus.Completed);
        after.Result.Should().Equal(before!.Result);
        after.Fingerprint.Should().Equal(Fingerprint.Hash.ToArray());
        after.RetentionUntil.Should().Be(before.RetentionUntil, "a refused admission writes nothing");

        var replay = await host.Operations.AdmitAsync(
            key,
            Fingerprint,
            expectedContract: Contract,
            retention: Retention,
            cancellationToken: AbortToken
        );

        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        Text(replay.Result!.Payload).Should().Be("stored");
        replay.Result.Contract.Should().Be(Contract);
    }

    public virtual async Task should_leave_no_record_when_an_enlisted_admission_rolls_back()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            var admission = await unit.Unit.Idempotency.AdmitAsync(
                key,
                Fingerprint,
                leaseDuration: LongLease,
                retention: Retention,
                cancellationToken: AbortToken
            );
            admission.IsAdmitted.Should().BeTrue();
            unit.Unit.IsRetryPrevented.Should().BeFalse("replaying an owned unit re-runs the admission");
            await unit.RollbackAsync();
        }

        (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().BeNull();

        var next = await AdmitAsync(host, key);

        next.IsAdmitted.Should().BeTrue();
        next.IsTakeover.Should().BeFalse();
    }

    public virtual async Task should_replay_within_retention_and_admit_fresh_once_it_passes()
    {
        var key = CreateKey();
        var retention = TimeSpan.FromDays(7);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await host.Operations.AdmitAsync(
            key,
            Fingerprint,
            leaseDuration: LongLease,
            retention: retention,
            cancellationToken: AbortToken
        );
        await host.Operations.CompleteAsync(first, Payload("kept"), Contract, cancellationToken: AbortToken);

        // A day and an hour later: past what a cache entry with a one-day TTL would have kept, inside retention.
        await Fixture.ShiftRecordIntoPastAsync(HostKey(key), TimeSpan.FromHours(25), AbortToken);
        var replay = await host.Operations.AdmitAsync(
            key,
            Fingerprint,
            retention: retention,
            cancellationToken: AbortToken
        );

        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        Text(replay.Result!.Payload).Should().Be("kept");

        // Past retention the stored outcome no longer binds the key, even for another fingerprint.
        await Fixture.ShiftRecordIntoPastAsync(HostKey(key), retention, AbortToken);
        var fresh = await host.Operations.AdmitAsync(
            key,
            OtherFingerprint,
            leaseDuration: LongLease,
            retention: retention,
            cancellationToken: AbortToken
        );

        fresh.IsAdmitted.Should().BeTrue();
        fresh.IsTakeover.Should().BeFalse("a completed record past retention names no unfinished attempt");
        fresh.Generation.Should().BeGreaterThan(first.Generation!.Value);

        var reset = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
        reset!.Status.Should().Be(IdempotencyRecordStatus.Pending);
        reset.Result.Should().BeNull();
        reset.ResultContract.Should().BeNull();
        reset.Fingerprint.Should().Equal(OtherFingerprint.Hash.ToArray());
        reset.Generation.Should().Be(fresh.Generation);
        reset.RetentionUntil.Should().BeAfter(DateTimeOffset.UtcNow + retention - TimeSpan.FromHours(1));
    }

    public virtual async Task should_refuse_the_expired_attempt_completion_after_a_takeover()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await AdmitAsync(host, key);
        await Fixture.ShiftLeaseIntoPastAsync(HostKey(key), LongLease + TimeSpan.FromMinutes(1), AbortToken);
        var second = await AdmitAsync(host, key);

        second.IsAdmitted.Should().BeTrue();
        second.IsTakeover.Should().BeTrue("the first attempt ended without completing or releasing");
        second.Generation.Should().BeGreaterThan(first.Generation!.Value);

        var late = async () =>
            await host.Operations.CompleteAsync(first, Payload("first"), Contract, cancellationToken: AbortToken);

        var refused = (await late.Should().ThrowAsync<StaleAdmissionException>()).Which;
        refused.Generation.Should().Be(first.Generation);
        refused.Reason.Should().Be(IdempotentLeaseStatus.Stale);

        await host.Operations.CompleteAsync(second, Payload("second"), Contract, cancellationToken: AbortToken);
        await late.Should().ThrowAsync<StaleAdmissionException>("the first attempt stays refused after the completion");

        var stored = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
        stored!.Status.Should().Be(IdempotencyRecordStatus.Completed);
        stored.Generation.Should().Be(second.Generation);
        stored.LeaseExpiresAt.Should().BeNull("a completed record holds no lease");
        Text(stored.Result!).Should().Be("second");

        var replay = await AdmitAsync(host, key);
        Text(replay.Result!.Payload).Should().Be("second");
    }

    public virtual async Task should_admit_again_at_once_after_a_release()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await AdmitAsync(host, key);

        (await host.Operations.ReleaseAsync(first, AbortToken)).Should().Be(IdempotentLeaseStatus.Released);

        var released = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
        released!.Status.Should().Be(IdempotencyRecordStatus.Pending);
        released.Generation.Should().BeNull();
        released.LeaseExpiresAt.Should().BeNull();

        var next = await AdmitAsync(host, key);

        next.IsAdmitted.Should().BeTrue("a released key needs no lease expiry before the next admission");
        next.IsTakeover.Should().BeFalse("a release is not a crash");
        next.Generation.Should().BeGreaterThan(first.Generation!.Value);
        (await host.Operations.ReleaseAsync(first, AbortToken))
            .Should()
            .Be(IdempotentLeaseStatus.Stale, "the released attempt no longer owns the record");
    }

    public virtual async Task should_keep_records_of_different_tenants_and_the_host_scope_independent()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        IdempotentAdmission tenantA;
        IdempotentAdmission tenantB;

        using (host.CurrentTenant.Change("tenant-a"))
        {
            tenantA = await AdmitAsync(host, key);
        }

        using (host.CurrentTenant.Change("tenant-b"))
        {
            tenantB = await AdmitAsync(host, key);
        }

        var hostScope = await AdmitAsync(host, key);

        tenantA.IsAdmitted.Should().BeTrue();
        tenantB.IsAdmitted.Should().BeTrue();
        hostScope.IsAdmitted.Should().BeTrue("the host scope is its own key namespace");
        tenantA.Key.TenantId.Should().Be("tenant-a");
        hostScope.Key.TenantId.Should().BeNull();

        await host.Operations.CompleteAsync(tenantA, Payload("a"), Contract, cancellationToken: AbortToken);

        using (host.CurrentTenant.Change("tenant-a"))
        {
            Text((await AdmitAsync(host, key)).Result!.Payload).Should().Be("a");
        }

        using (host.CurrentTenant.Change("tenant-b"))
        {
            (await AdmitAsync(host, key)).Disposition.Should().Be(IdempotentDisposition.InFlight);
        }

        (await Fixture.ReadRecordAsync(new IdempotencyRecordKey("tenant-a", key), AbortToken))!
            .Status.Should()
            .Be(IdempotencyRecordStatus.Completed);
        (await Fixture.ReadRecordAsync(new IdempotencyRecordKey("tenant-b", key), AbortToken))!
            .Generation.Should()
            .Be(tenantB.Generation);
        (await Fixture.ReadRecordAsync(HostKey(key), AbortToken))!.Generation.Should().Be(hostScope.Generation);
    }

    public virtual async Task should_refuse_keys_with_surrounding_whitespace_before_any_write()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var admitted = await AdmitAsync(host, key);
        var before = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);

        // SQL Server compares nvarchar with trailing spaces padded away, so "k " would land on the record of "k" (a
        // read of "k " there even returns it), and a padded admission would report the unpadded key in flight.
        foreach (var padded in (string[])[key + " ", " " + key, key + "\t"])
        {
            var admit = async () => await AdmitAsync(host, padded);

            await admit.Should().ThrowAsync<ArgumentException>();
        }

        var stored = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
        stored.Should().BeEquivalentTo(before, "a refused key writes nothing");
        stored!.Generation.Should().Be(admitted.Generation);
        (await Fixture.ReadRecordAsync(HostKey(" " + key), AbortToken))
            .Should()
            .BeNull("a leading space is never padded away, so no record exists under it");
    }

    public virtual async Task should_refuse_a_second_completion_by_the_same_attempt()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var admitted = await AdmitAsync(host, key);
        await host.Operations.CompleteAsync(admitted, Payload("first"), Contract, cancellationToken: AbortToken);
        var before = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);

        var again = async () =>
            await host.Operations.CompleteAsync(admitted, Payload("second"), Contract, cancellationToken: AbortToken);

        (await again.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Completed);

        var after = await Fixture.ReadRecordAsync(HostKey(key), AbortToken);
        Text(after!.Result!).Should().Be("first", "the stored result is never overwritten");
        after.RetentionUntil.Should().Be(before!.RetentionUntil, "a refused completion writes nothing");
    }

    public virtual async Task should_renew_only_while_the_attempt_owns_the_key()
    {
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        // Live: the expiry moves forward by the database clock.
        var liveKey = CreateKey();
        var live = await AdmitAsync(host, liveKey);
        var renewed = await host.Operations.RenewAsync(live, TimeSpan.FromMinutes(30), AbortToken);

        renewed.IsRenewed.Should().BeTrue();
        renewed.ExpiresAt.Should().BeAfter(live.LeaseExpiresAt!.Value);
        (await Fixture.ReadRecordAsync(HostKey(liveKey), AbortToken))!
            .LeaseExpiresAt.Should()
            .BeCloseTo(renewed.ExpiresAt!.Value, TimeSpan.FromMicroseconds(1));

        // Expired: not extended, and the past expiry is reported.
        var expiredKey = CreateKey();
        var expired = await AdmitAsync(host, expiredKey);
        await Fixture.ShiftLeaseIntoPastAsync(HostKey(expiredKey), LongLease + TimeSpan.FromMinutes(1), AbortToken);
        var refused = await host.Operations.RenewAsync(expired, LongLease, AbortToken);

        refused.Status.Should().Be(IdempotentLeaseStatus.Expired);
        refused.ExpiresAt.Should().NotBeNull();

        // Taken over: the expired attempt is stale once another admission drew a newer generation.
        var takeover = await AdmitAsync(host, expiredKey);
        takeover.IsTakeover.Should().BeTrue();
        (await host.Operations.RenewAsync(expired, LongLease, AbortToken))
            .Should()
            .Be(new IdempotentLeaseRenewal(IdempotentLeaseStatus.Stale, ExpiresAt: null));

        // Completed and released attempts report so.
        await host.Operations.CompleteAsync(takeover, Payload("done"), Contract, cancellationToken: AbortToken);
        (await host.Operations.RenewAsync(takeover, LongLease, AbortToken))
            .Status.Should()
            .Be(IdempotentLeaseStatus.Completed);

        var releasedKey = CreateKey();
        var released = await AdmitAsync(host, releasedKey);
        await host.Operations.ReleaseAsync(released, AbortToken);
        (await host.Operations.RenewAsync(released, LongLease, AbortToken))
            .Status.Should()
            .Be(IdempotentLeaseStatus.Released);
    }

    /// <summary>
    /// A fence inside a unit that began while the lease was live must still refuse once the lease expired: every
    /// decision reads the database clock after the row lock, never a clock frozen at transaction start.
    /// </summary>
    public virtual async Task should_refuse_a_fence_in_a_long_enlisted_unit_once_the_lease_expired()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var lease = TimeSpan.FromSeconds(2);

        var admitted = await host.Operations.AdmitAsync(
            key,
            Fingerprint,
            leaseDuration: lease,
            retention: Retention,
            cancellationToken: AbortToken
        );

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        await Fixture.TouchAsync(unit.Unit, AbortToken);
        await Task.Delay(lease + TimeSpan.FromSeconds(1), AbortToken);

        var fence = async () => await unit.Unit.Idempotency.FenceAsync(admitted, AbortToken);

        (await fence.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Expired);
    }

    #endregion

    #region Fencing

    public virtual async Task should_make_an_admission_wait_for_an_enlisted_fence_and_then_replay()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var admitted = await AdmitAsync(host, key);

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        await unit.Unit.Idempotency.FenceAsync(admitted, AbortToken);

        var pending = AdmitAsync(host, key);
        var first = await Task.WhenAny(
            pending,
            Task.Delay(IdempotencyFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().NotBeSameAs(pending, "the admission waits on the record the fence holds");

        await unit.Unit.Idempotency.CompleteAsync(admitted, Payload("fenced"), Contract, cancellationToken: AbortToken);
        await unit.CommitAsync(AbortToken);
        var next = await pending.WaitAsync(IdempotencyFixtureExtensions.ReleaseTimeout, AbortToken);

        next.Disposition.Should().Be(IdempotentDisposition.Replay);
        Text(next.Result!.Payload).Should().Be("fenced");
    }

    public virtual async Task should_not_deadlock_admissions_racing_an_enlisted_fence_then_complete()
    {
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        // The fence holds the record's update-intent lock while the unit completes, so every admission waits on that
        // one row and then replays. A shared-lock fence would deadlock here: a waiting admission queues for the
        // update lock, and the fencing unit's own completion then waits behind it. Any deadlock surfaces as an
        // exception out of Task.WhenAll.
        for (var round = 0; round < 10; round++)
        {
            var key = CreateKey();
            var admitted = await AdmitAsync(host, key);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var complete = Task.Run(
                async () =>
                {
                    await start.Task;
                    await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
                    await unit.Unit.Idempotency.FenceAsync(admitted, AbortToken);
                    await Task.Yield();
                    await unit.Unit.Idempotency.CompleteAsync(
                        admitted,
                        Payload("done"),
                        Contract,
                        cancellationToken: AbortToken
                    );
                    await unit.CommitAsync(AbortToken);
                },
                AbortToken
            );

            var admissions = Enumerable
                .Range(0, 4)
                .Select(_ =>
                    Task.Run(
                        async () =>
                        {
                            await start.Task;

                            return await AdmitAsync(host, key);
                        },
                        AbortToken
                    )
                )
                .ToList();

            start.SetResult();
            await complete;
            var results = await Task.WhenAll(admissions);

            results
                .Should()
                .AllSatisfy(r =>
                    r.Disposition.Should().BeOneOf(IdempotentDisposition.InFlight, IdempotentDisposition.Replay)
                );
            (await Fixture.ReadRecordAsync(HostKey(key), AbortToken))!
                .Status.Should()
                .Be(IdempotencyRecordStatus.Completed);
        }
    }

    #endregion

    #region Peek

    public virtual async Task should_peek_absent_pending_completed_and_respect_retention_and_tenant_scope()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        (await host.Operations.PeekAsync(key, AbortToken)).Should().Be(IdempotencyPeekStatus.Absent);

        var admitted = await AdmitAsync(host, key);

        (await host.Operations.PeekAsync(key, AbortToken)).Should().Be(IdempotencyPeekStatus.Pending);

        await host.Operations.CompleteAsync(admitted, Payload("done"), Contract, cancellationToken: AbortToken);

        (await host.Operations.PeekAsync(key, AbortToken)).Should().Be(IdempotencyPeekStatus.Completed);

        await Fixture.ShiftRecordIntoPastAsync(HostKey(key), Retention + TimeSpan.FromHours(1), AbortToken);

        (await host.Operations.PeekAsync(key, AbortToken))
            .Should()
            .Be(IdempotencyPeekStatus.Absent, "a record past its retention peeks as absent");

        using (host.CurrentTenant.Change("tenant-a"))
        {
            (await host.Operations.PeekAsync(key, AbortToken))
                .Should()
                .Be(IdempotencyPeekStatus.Absent, "a peek is scoped to the current tenant");
        }
    }

    /// <summary>
    /// Holds the record under a write lock that outlives its own statement (an enlisted, uncommitted completion), so
    /// only a provider whose reads never wait behind that lock returns before the unit ends.
    /// </summary>
    public virtual async Task should_peek_without_waiting_on_an_uncommitted_write_to_the_record()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var admitted = await AdmitAsync(host, key);

        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        await unit.Unit.Idempotency.FenceAsync(admitted, AbortToken);
        await unit.Unit.Idempotency.CompleteAsync(admitted, Payload("done"), Contract, cancellationToken: AbortToken);

        var peek = host.Operations.PeekAsync(key, AbortToken).AsTask();
        var first = await Task.WhenAny(
            peek,
            Task.Delay(IdempotencyFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().BeSameAs(peek, "a peek never takes or waits on the record's row lock");
        (await peek).Should().Be(IdempotencyPeekStatus.Pending, "the completing transaction has not committed yet");

        await unit.CommitAsync(AbortToken);

        (await host.Operations.PeekAsync(key, AbortToken)).Should().Be(IdempotencyPeekStatus.Completed);
    }

    #endregion

    #region Purge

    public virtual async Task should_purge_only_records_past_retention_whose_lease_is_not_live()
    {
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var past = Retention + TimeSpan.FromHours(1);

        var completedOld = CreateKey();
        await host.Operations.CompleteAsync(
            await AdmitAsync(host, completedOld),
            Payload("old"),
            Contract,
            cancellationToken: AbortToken
        );
        await Fixture.ShiftRecordIntoPastAsync(HostKey(completedOld), past, AbortToken);

        var releasedOld = CreateKey();
        await host.Operations.ReleaseAsync(await AdmitAsync(host, releasedOld), AbortToken);
        await Fixture.ShiftRecordIntoPastAsync(HostKey(releasedOld), past, AbortToken);

        // An attempt that crashed: its lease expired without a completion or a release.
        var abandonedOld = CreateKey();
        await AdmitAsync(host, abandonedOld);
        await Fixture.ShiftLeaseIntoPastAsync(HostKey(abandonedOld), LongLease + TimeSpan.FromMinutes(1), AbortToken);
        await Fixture.ShiftRecordIntoPastAsync(HostKey(abandonedOld), past, AbortToken);

        // An attempt whose lease outlived the record's retention: it may still complete, so its record stays.
        var liveOld = CreateKey();
        var liveOldAttempt = await AdmitAsync(host, liveOld);
        await Fixture.ShiftRecordIntoPastAsync(HostKey(liveOld), past, AbortToken);

        var inFlight = CreateKey();
        var live = await AdmitAsync(host, inFlight);

        var completedFresh = CreateKey();
        await host.Operations.CompleteAsync(
            await AdmitAsync(host, completedFresh),
            Payload("fresh"),
            Contract,
            cancellationToken: AbortToken
        );

        string[] doomed = [completedOld, releasedOld, abandonedOld];
        string[] kept = [liveOld, inFlight, completedFresh];

        // An age cutoff further back than any of them spares them all.
        await _PurgeAllAsync(host, olderThan: past + TimeSpan.FromHours(1));

        foreach (var key in doomed.Concat(kept))
        {
            (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().NotBeNull();
        }

        (await host.Store.PurgeAsync(TimeSpan.Zero, limit: 1, AbortToken)).Should().Be(1, "the limit bounds a call");
        await _PurgeAllAsync(host, olderThan: TimeSpan.Zero);

        foreach (var key in doomed)
        {
            (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().BeNull($"'{key}' is past retention");
        }

        foreach (var key in kept)
        {
            (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().NotBeNull($"'{key}' is retained");
        }

        (await Fixture.ReadRecordAsync(HostKey(inFlight), AbortToken))!.Generation.Should().Be(live.Generation);
        (await Fixture.ReadRecordAsync(HostKey(liveOld), AbortToken))!
            .Generation.Should()
            .Be(liveOldAttempt.Generation);
    }

    public virtual async Task should_keep_a_live_attempt_record_past_retention_until_its_lease_expires()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var live = await AdmitAsync(host, key);
        await Fixture.ShiftRecordIntoPastAsync(HostKey(key), Retention + TimeSpan.FromHours(1), AbortToken);
        await _PurgeAllAsync(host, olderThan: TimeSpan.Zero);

        (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().NotBeNull("its lease is still live");

        var other = await host.Operations.AdmitAsync(
            key,
            OtherFingerprint,
            leaseDuration: LongLease,
            retention: Retention,
            cancellationToken: AbortToken
        );

        other
            .Disposition.Should()
            .Be(IdempotentDisposition.Conflict, "a live attempt keeps its record bound even past retention");
        (await AdmitAsync(host, key)).Disposition.Should().Be(IdempotentDisposition.InFlight);

        // Once the lease expires the purge takes the record, and the late attempt can no longer complete.
        await Fixture.ShiftLeaseIntoPastAsync(HostKey(key), LongLease + TimeSpan.FromMinutes(1), AbortToken);
        await _PurgeAllAsync(host, olderThan: TimeSpan.Zero);

        (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().BeNull();

        var complete = async () =>
            await host.Operations.CompleteAsync(live, Payload("late"), Contract, cancellationToken: AbortToken);

        (await complete.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Stale);
    }

    public virtual async Task should_draw_a_higher_generation_after_the_record_was_purged()
    {
        var key = CreateKey();
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var first = await AdmitAsync(host, key);
        await host.Operations.CompleteAsync(first, Payload("first"), Contract, cancellationToken: AbortToken);
        await Fixture.ShiftRecordIntoPastAsync(HostKey(key), Retention + TimeSpan.FromHours(1), AbortToken);
        await _PurgeAllAsync(host, olderThan: TimeSpan.Zero);

        (await Fixture.ReadRecordAsync(HostKey(key), AbortToken)).Should().BeNull();

        var second = await AdmitAsync(host, key);

        second.IsAdmitted.Should().BeTrue();
        second.IsTakeover.Should().BeFalse("the purged record named no unfinished attempt");
        second
            .Generation.Should()
            .BeGreaterThan(first.Generation!.Value, "generations come from one store-wide sequence, not the row");

        // The zombie of the purged record can never match the new one.
        var zombie = async () =>
            await host.Operations.CompleteAsync(first, Payload("zombie"), Contract, cancellationToken: AbortToken);

        await zombie.Should().ThrowAsync<StaleAdmissionException>();
    }

    public virtual async Task should_purge_records_from_the_retention_service()
    {
        await using var host = await Fixture.CreateHostAsync(
            setup =>
                setup.ConfigureOptions(options =>
                {
                    options.PurgeInterval = TimeSpan.FromMilliseconds(100);
                    options.DefaultRetention = Retention;
                }),
            AbortToken
        );
        var past = Retention + TimeSpan.FromHours(1);

        var completed = CreateKey();
        await host.Operations.CompleteAsync(
            await AdmitAsync(host, completed),
            Payload("old"),
            Contract,
            cancellationToken: AbortToken
        );
        await Fixture.ShiftRecordIntoPastAsync(HostKey(completed), past, AbortToken);

        var inFlight = CreateKey();
        await AdmitAsync(host, inFlight);

        await host.RetentionService.StartAsync(AbortToken);

        try
        {
            var deadline = DateTimeOffset.UtcNow + IdempotencyFixtureExtensions.ReleaseTimeout;

            while (await Fixture.ReadRecordAsync(HostKey(completed), AbortToken) is not null)
            {
                DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the retention service purges on its interval");
                await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
            }
        }
        finally
        {
            await host.RetentionService.StopAsync(AbortToken);
        }

        (await Fixture.ReadRecordAsync(HostKey(inFlight), AbortToken)).Should().NotBeNull();
    }

    #endregion

    /// <summary>The stored key of a host-scope record.</summary>
    protected static IdempotencyRecordKey HostKey(string key)
    {
        return new IdempotencyRecordKey("", key);
    }

    /// <summary>Returns an idempotency key no other test uses.</summary>
    protected static string CreateKey()
    {
        return $"key-{Guid.NewGuid():N}";
    }

    protected static ReadOnlyMemory<byte> Payload(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    protected static string Text(ReadOnlyMemory<byte> payload)
    {
        return Encoding.UTF8.GetString(payload.Span);
    }

    /// <summary>Admits <paramref name="key" /> autonomously with the suite's fingerprint, lease, and retention.</summary>
    protected Task<IdempotentAdmission> AdmitAsync(IdempotencyHost host, string key)
    {
        return host
            .Operations.AdmitAsync(
                key,
                Fingerprint,
                leaseDuration: LongLease,
                retention: Retention,
                cancellationToken: AbortToken
            )
            .AsTask();
    }

    private async Task _PurgeAllAsync(IdempotencyHost host, TimeSpan olderThan)
    {
        const int batch = 1000;

        while (await host.Store.PurgeAsync(olderThan, batch, AbortToken) >= batch) { }
    }
}
