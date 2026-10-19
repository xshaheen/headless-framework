// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Tests.Guarantees;

namespace Tests;

/// <summary>
/// The dispatch lease of a claimed retry row: re-stamped when its attempt is reserved, slid forward while the attempt
/// runs, and never revived once another node holds it.
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
        var batchLease = lateRow.LockedUntil!.Value;

        // when: the rows before it use most of the batch lease, then the late row reserves its attempt
        await _ElapseAsync(TimeSpan.FromSeconds(1.5));
        lateRow.InlineAttempts++;
        var reserved = published
            ? await storage.ReservePublishAttemptAsync(lateRow, originalInlineAttempts: 0, AbortToken)
            : await storage.ReserveReceiveAttemptAsync(lateRow, originalInlineAttempts: 0, AbortToken);

        // then: the attempt starts with a full lease, and the caller holds the stored identity
        reserved.Should().BeTrue();
        lateRow.LockedUntil.Should().BeAfter(batchLease.AddSeconds(1));
        if (await GetPersistedLeaseIdentityAsync(published, lateRow.StorageId, AbortToken) is { } persisted)
        {
            lateRow.LockedUntil.Should().Be(persisted.LockedUntil);
            lateRow.Owner.Should().Be(persisted.Owner);
        }

        // and: once the batch lease has passed, another node reclaims the untouched row but not the reserved one
        await _ElapseAsync(TimeSpan.FromSeconds(1));
        NodeMembership.SetIdentity($"batch-contender-{published}");
        var reclaimed = (await _PickUpRetriesAsync(storage, published)).Select(m => m.StorageId).ToList();
        reclaimed.Should().Contain(early.StorageId).And.NotContain(late.StorageId);
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

        // when: the attempt renews before its lease ends
        await _ElapseAsync(TimeSpan.FromSeconds(1.5));
        var renewed = await _RenewAsync(storage, published, message, lease);

        // then: the lease slides forward and the caller holds the stored identity
        renewed.Should().BeTrue();
        message.LockedUntil.Should().BeAfter(staleHolder.LockedUntil!.Value.AddSeconds(1));
        if (await GetPersistedLeaseIdentityAsync(published, message.StorageId, AbortToken) is { } persisted)
        {
            message.LockedUntil.Should().Be(persisted.LockedUntil);
        }

        // and: a holder of the replaced generation cannot renew it
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
        var heldLease = message.LockedUntil;
        await _ElapseAsync(TimeSpan.FromMilliseconds(750));
        NodeMembership.SetIdentity($"lapsed-taker-{published}");
        (await _LeaseAsync(storage, published, _CopyLease(message), TimeSpan.FromMinutes(1))).Should().BeTrue();

        // when
        var renewed = await _RenewAsync(storage, published, message, TimeSpan.FromMinutes(1));

        // then: the renewal fails and leaves the caller's identity as it was
        renewed.Should().BeFalse();
        message.LockedUntil.Should().Be(heldLease);
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
            .LockedUntil.Should()
            .BeOnOrAfter(databaseTimeBefore.Add(lease))
            .And.BeOnOrBefore(databaseTimeAfter.Add(lease));
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
            LockedUntil = message.LockedUntil,
            Owner = message.Owner,
            InboxAttemptFence = message.InboxAttemptFence,
        };
    }
}
