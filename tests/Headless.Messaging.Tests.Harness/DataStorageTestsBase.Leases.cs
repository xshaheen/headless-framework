// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Tests.Guarantees;

namespace Tests;

/// <summary>
/// The dispatch lease of a claimed retry row: renewed when its attempt is reserved, slid forward while the attempt
/// runs, and never revived once another node holds it. The lease is a fenced row lease, so every write a holder makes
/// names its generation and a replaced generation writes nothing.
/// </summary>
public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_restamp_the_lease_when_an_attempt_is_reserved_late_in_a_claimed_batch(
        bool published
    )
    {
        // given: one claim leases two rows for a two-second batch lease
        if (!TrySetDispatchTimeout(TimeSpan.FromSeconds(2)))
        {
            Assert.Skip("Storage does not expose a mutable dispatch timeout");
        }

        var storage = GetStorage();
        NodeMembership.SetIdentity($"batch-claimer-{published}");
        var early = await _StoreRetryDueAsync(storage, published, "late-reserve-early");
        var late = await _StoreRetryDueAsync(storage, published, "late-reserve-late");
        var claimed = (await _PickUpRetriesAsync(storage, published)).ToList();
        claimed.Select(m => m.StorageId).Should().Contain([early.StorageId, late.StorageId]);
        var lateRow = claimed.Single(m => m.StorageId == late.StorageId);
        var batchLease = lateRow.Lease.ExpiresAt!.Value;

        // when: the rows before it use most of the batch lease, then the late row reserves its attempt
        await _ElapseAsync(TimeSpan.FromSeconds(1.5));
        lateRow.InlineAttempts++;
        var reserved = published
            ? await storage.ReservePublishAttemptAsync(lateRow, originalInlineAttempts: 0, AbortToken)
            : await storage.ReserveReceiveAttemptAsync(lateRow, originalInlineAttempts: 0, AbortToken);

        // then: the attempt starts with a full lease, and the caller holds the stored identity
        reserved.Should().BeTrue();
        lateRow.Lease.ExpiresAt.Should().BeAfter(batchLease.AddSeconds(1));
        if (await GetPersistedLeaseIdentityAsync(published, lateRow.StorageId, AbortToken) is { } persisted)
        {
            lateRow.Lease.Generation.Should().Be(persisted.LeaseGeneration);
            lateRow.Lease.ExpiresAt.Should().Be(persisted.LeaseExpiresAt);
            lateRow.Lease.Holder.Should().Be(persisted.LeaseHolder);
        }

        // and: once the batch lease has passed, another node reclaims the untouched row but not the reserved one
        await _ElapseAsync(TimeSpan.FromSeconds(1));
        NodeMembership.SetIdentity($"batch-contender-{published}");
        var reclaimed = (await _PickUpRetriesAsync(storage, published)).Select(m => m.StorageId).ToList();
        reclaimed.Should().Contain(early.StorageId).And.NotContain(late.StorageId);
    }

    /// <summary>
    /// The SQL that expires the lease of the row at <c>@Id</c> in the published or received table, run inside a
    /// reservation's own transaction; <see langword="null"/> for a store with no such seam.
    /// </summary>
    protected virtual string? ExpireLeaseSql(bool published) => null;

    public virtual async Task should_leave_the_attempt_counter_unchanged_when_the_reservation_cannot_renew_the_lease(
        bool published
    )
    {
        // given: a claimed row, and a lease that runs out between the reservation's counter write and its renewal
        if (ExpireLeaseSql(published) is not { } expireLease || GetStorage() is not RelationalDataStorage storage)
        {
            Assert.Skip("Storage has no seam between the reservation's write and its renewal");
            return;
        }

        NodeMembership.SetIdentity($"reserve-lapse-{published}");
        var stored = await _StoreRetryDueAsync(storage, published, "reserve-lapse");
        var claimed = (await _PickUpRetriesAsync(storage, published)).Single(m => m.StorageId == stored.StorageId);
        storage.BeforeReserveRenewal = async (connection, transaction, storageId, ct) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
#pragma warning disable CA2100 // The provider's own test SQL; the row id is a parameter.
            command.CommandText = expireLease;
#pragma warning restore CA2100
            var id = command.CreateParameter();
            id.ParameterName = "@Id";
            id.Value = storageId;
            command.Parameters.Add(id);
            await command.ExecuteNonQueryAsync(ct);
        };

        try
        {
            // when: the row reserves its next attempt
            claimed.InlineAttempts++;
            var reserved = published
                ? await storage.ReservePublishAttemptAsync(claimed, originalInlineAttempts: 0, AbortToken)
                : await storage.ReserveReceiveAttemptAsync(claimed, originalInlineAttempts: 0, AbortToken);

            // then: the reservation is refused
            reserved.Should().BeFalse();
        }
        finally
        {
            storage.BeforeReserveRenewal = null;
        }

        // and: the refused reservation wrote nothing, so the same attempt reserves against the counter it read. The
        // expiry ran inside the refused transaction and rolled back with it, so the lease is the claim's again.
        var retried = published
            ? await storage.ReservePublishAttemptAsync(claimed, originalInlineAttempts: 0, AbortToken)
            : await storage.ReserveReceiveAttemptAsync(claimed, originalInlineAttempts: 0, AbortToken);
        retried.Should().BeTrue();
    }

    [Guarantee("MSG-LEASE-02")]
    public virtual async Task should_renew_only_the_live_lease_generation_the_caller_holds(bool published)
    {
        // given: an attempt holds a two-second lease
        var storage = GetStorage();
        NodeMembership.SetIdentity($"renewing-owner-{published}");
        var message = await _StoreRetryDueAsync(storage, published, "renew-live");
        var lease = TimeSpan.FromSeconds(2);
        (await _LeaseAsync(storage, published, message, lease)).Should().BeTrue();
        var staleHolder = _CopyLease(message);
        staleHolder.Lease = staleHolder.Lease with { Generation = staleHolder.Lease.Generation - 1 };
        var leaseBefore = message.Lease.ExpiresAt!.Value;

        // when: the attempt renews before its lease ends
        await _ElapseAsync(TimeSpan.FromSeconds(1.5));
        var renewed = await _RenewAsync(storage, published, message, lease);

        // then: the lease slides forward and the caller holds the stored identity
        renewed.Should().BeTrue();
        message.Lease.ExpiresAt.Should().BeAfter(leaseBefore.AddSeconds(1));
        if (await GetPersistedLeaseIdentityAsync(published, message.StorageId, AbortToken) is { } persisted)
        {
            message.Lease.ExpiresAt.Should().Be(persisted.LeaseExpiresAt);
            message.Lease.Generation.Should().Be(persisted.LeaseGeneration);
        }

        // and: a holder of an earlier generation cannot renew it
        (await _RenewAsync(storage, published, staleHolder, lease))
            .Should()
            .BeFalse();

        // and: past the original lease, another node cannot take the row
        await _ElapseAsync(TimeSpan.FromSeconds(1));
        NodeMembership.SetIdentity($"renew-contender-{published}");
        (await _LeaseAsync(storage, published, _CopyLease(message), lease)).Should().BeFalse();
        (await _PickUpRetriesAsync(storage, published)).Should().NotContain(m => m.StorageId == message.StorageId);
    }

    [Guarantee("MSG-LEASE-02")]
    public virtual async Task should_not_renew_a_lease_that_lapsed_and_another_node_took(bool published)
    {
        // given: an attempt's lease lapsed and another node took the row
        var storage = GetStorage();
        NodeMembership.SetIdentity($"lapsed-owner-{published}");
        var message = await _StoreRetryDueAsync(storage, published, "renew-lapsed");
        (await _LeaseAsync(storage, published, message, TimeSpan.FromMilliseconds(500))).Should().BeTrue();
        var heldLease = message.Lease;
        await _ElapseAsync(TimeSpan.FromMilliseconds(750));
        NodeMembership.SetIdentity($"lapsed-taker-{published}");
        (await _LeaseAsync(storage, published, _CopyLease(message), TimeSpan.FromMinutes(1))).Should().BeTrue();

        // when
        var renewed = await _RenewAsync(storage, published, message, TimeSpan.FromMinutes(1));

        // then: the renewal fails and leaves the caller's identity as it was
        renewed.Should().BeFalse();
        message.Lease.Should().Be(heldLease);
    }

    public virtual async Task should_stamp_reserved_and_renewed_leases_from_the_database_clock(
        bool published,
        bool renew
    )
    {
        // given: a node whose application clock runs an hour ahead of the database
        var skewedStorage = _CreateRelationalClockSkewStorage();
        var lease = TimeSpan.FromSeconds(5);
        if (!TrySetDispatchTimeout(lease))
        {
            Assert.Skip("Storage does not expose a mutable dispatch timeout");
        }

        var storage = GetStorage();
        NodeMembership.SetIdentity($"skewed-renewer-{published}-{renew}");
        var message = await _StoreRetryDueAsync(storage, published, "db-clock-renew");
        (await _LeaseAsync(skewedStorage, published, message, lease)).Should().BeTrue();
        var databaseTimeBefore = await _GetDatabaseUtcNowOrSkipAsync();

        // when
        bool applied;
        if (renew)
        {
            applied = await _RenewAsync(skewedStorage, published, message, lease);
        }
        else
        {
            message.InlineAttempts++;
            applied = published
                ? await skewedStorage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken)
                : await skewedStorage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken);
        }

        var databaseTimeAfter = await _GetDatabaseUtcNowOrSkipAsync();

        // then: the deadline is one lease past the database's clock, not the node's
        applied.Should().BeTrue();
        message
            .Lease.ExpiresAt.Should()
            .BeOnOrAfter(databaseTimeBefore.Add(lease))
            .And.BeOnOrBefore(databaseTimeAfter.Add(lease));
    }

    [Guarantee("MSG-LEASE-02")]
    public virtual async Task should_refuse_every_write_from_a_holder_whose_lease_was_granted_again(bool published)
    {
        // given: holder A's lease lapsed and holder B leased the row again
        var storage = GetStorage();
        NodeMembership.SetIdentity($"re-leased-stale-{published}");
        var stale = await _StoreRetryDueAsync(storage, published, "re-leased");
        (await _LeaseAsync(storage, published, stale, _ShortLease)).Should().BeTrue();
        await _ElapseAsync(_ShortLease * 5);
        var successor = _CopyLease(stale);
        NodeMembership.SetIdentity($"re-leased-successor-{published}");
        (await _LeaseAsync(storage, published, successor, TimeSpan.FromMinutes(1))).Should().BeTrue();
        successor.Lease.Generation.Should().BeGreaterThan(stale.Lease.Generation);
        var staleIdentity = new MessageLeaseIdentity(
            stale.StorageId,
            stale.Lease.Generation,
            stale.Lane,
            stale.InboxAttemptFence
        );

        // when: A writes its outcome, releases the lease, and (for an inbox row) defers its retry
        var stateWritten = await _WriteFencedOutcomeAsync(storage, published, stale);
        var releaser = storage.Should().BeAssignableTo<IGracefulLeaseReleaseStorage>().Subject;
        var released = published
            ? await releaser.ReleasePublishedLeaseAsync(staleIdentity, AbortToken)
            : await releaser.ReleaseReceivedLeaseAsync(staleIdentity, AbortToken);

        // then: the database refuses every write of the replaced generation
        stateWritten.Should().BeFalse("a replaced lease generation must not write the row's outcome");
        released.Should().BeFalse("a replaced lease generation must not end its successor's lease");
        if (!published && storage is ICircuitRetryDeferralStorage deferrer)
        {
            (
                await deferrer.DeferReceivedRetryAsync(
                    new CircuitRetryDeferral(staleIdentity, TimeSpan.FromMinutes(5)),
                    AbortToken
                )
            )
                .Should()
                .BeFalse("a replaced lease generation must not defer its successor's retry");
        }

        // and: B still holds the row and writes its outcome
        if (await GetPersistedLeaseIdentityAsync(published, successor.StorageId, AbortToken) is { } persisted)
        {
            persisted.LeaseGeneration.Should().Be(successor.Lease.Generation);
            persisted.LeaseState.Should().Be((short)RowLeaseState.Active);
            persisted.LeaseHolder.Should().Be(successor.Lease.Holder);
        }

        var successorWritten = await _WriteFencedOutcomeAsync(storage, published, successor);
        successorWritten.Should().BeTrue();
    }

    /// <summary>
    /// Writes a delivery's outcome the way the dispatcher ends an attempt: through the fenced state write, which only the
    /// lease generation the message carries may apply.
    /// </summary>
    private static async Task<bool> _WriteFencedOutcomeAsync(
        IDataStorage storage,
        bool published,
        MediumMessage message
    )
    {
        return published
            ? await storage.ChangePublishRetryStateAsync(
                message,
                StatusName.Succeeded,
                MessageContentWrite.Preserve,
                retryDelay: null,
                keepLease: false,
                message.Retries,
                message.InlineAttempts,
                AbortToken
            )
            : await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Succeeded,
                MessageContentWrite.Preserve,
                retryDelay: null,
                keepLease: false,
                message.Retries,
                message.InlineAttempts,
                AbortToken
            );
    }

    private async Task<MediumMessage> _StoreRetryDueAsync(IDataStorage storage, bool published, string name)
    {
        var message = published
            ? await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken)
            : await storage.StoreReceivedMessageAsync(name, $"{name}-group", CreateMessage(), AbortToken);
        var scheduled = published
            ? await storage.ChangePublishStateAsync(
                message,
                StatusName.Failed,
                retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
                cancellationToken: AbortToken
            )
            : await storage.ChangeReceiveStateAsync(
                message,
                StatusName.Failed,
                retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
                cancellationToken: AbortToken
            );
        scheduled.Should().BeTrue();

        return message;
    }

    private static async Task<bool> _LeaseAsync(
        IDataStorage storage,
        bool published,
        MediumMessage message,
        TimeSpan lease
    )
    {
        return published
            ? await storage.LeasePublishAsync(message, lease, AbortToken)
            : await storage.LeaseReceiveAsync(message, lease, AbortToken);
    }

    private static async Task<bool> _RenewAsync(
        IDataStorage storage,
        bool published,
        MediumMessage message,
        TimeSpan lease
    )
    {
        return published
            ? await storage.RenewPublishLeaseAsync(message, lease, AbortToken)
            : await storage.RenewReceiveLeaseAsync(message, lease, AbortToken);
    }

    /// <summary>Another holder of the same row and lease generation, so a write through it cannot move the original.</summary>
    private static MediumMessage _CopyLease(MediumMessage message)
    {
        return new MediumMessage
        {
            StorageId = message.StorageId,
            Origin = message.Origin,
            Content = message.Content,
            Lane = message.Lane,
            Retries = message.Retries,
            InlineAttempts = message.InlineAttempts,
            Lease = message.Lease,
            InboxAttemptFence = message.InboxAttemptFence,
        };
    }
}
