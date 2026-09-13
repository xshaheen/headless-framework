// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_revoke_delayed_message_and_prevent_reservation_and_shutdown_resurrection()
    {
        var storage = GetStorage();
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromDays(1));
        var revocation = (IMessageRevocationStorage)storage;

        (await revocation.RevokeAsync(message.StorageId, AbortToken)).Should().Be(MessageRevocationResult.Revoked);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().BeNull();
        message.InlineAttempts = 1;
        (await storage.LeasePublishAndReserveAttemptAsync(message, TimeSpan.FromMinutes(1), 0, AbortToken))
            .Should()
            .BeFalse();
        await storage.ChangePublishStateToDelayedAsync([message.StorageId], AbortToken);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().BeNull();
        (await revocation.RevokeAsync(message.StorageId, AbortToken)).Should().Be(MessageRevocationResult.NotFound);
    }

    public virtual async Task should_revoke_queued_message_before_first_reservation()
    {
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromSeconds(30));
        (await ((IMessageRevocationStorage)GetStorage()).RevokeAsync(message.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.Revoked);
    }

    public virtual async Task should_reject_revocation_after_attempt_reservation()
    {
        var storage = GetStorage();
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromDays(1));
        message.InlineAttempts = 1;
        (await storage.LeasePublishAndReserveAttemptAsync(message, TimeSpan.FromMinutes(1), 0, AbortToken))
            .Should()
            .BeTrue();
        (await ((IMessageRevocationStorage)storage).RevokeAsync(message.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.AttemptReserved);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().NotBeNull();
    }

    public virtual async Task should_not_revoke_pending_persisted_retry_with_reset_inline_counter()
    {
        var storage = GetStorage();
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromDays(1));
        message.Retries = 1;
        message.InlineAttempts = 0;
        (
            await storage.ChangePublishStateAsync(
                message,
                StatusName.Failed,
                nextRetryAt: TimeProvider.GetUtcNow().AddMinutes(5),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
        (await ((IMessageRevocationStorage)storage).RevokeAsync(message.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.AttemptReserved);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().NotBeNull();
    }

    public virtual async Task should_revoke_claimed_but_unreserved_message()
    {
        var storage = GetStorage();
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromSeconds(90));
        var claimed = await ((IDelayedMessageClaimStorage)storage).ClaimDelayedMessagesAsync(AbortToken);
        var candidate = claimed.Single(x => x.StorageId == message.StorageId);
        (await ((IMessageRevocationStorage)storage).RevokeAsync(candidate.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.Revoked);
        candidate.InlineAttempts = 1;
        (await storage.ReservePublishAttemptAsync(candidate, 0, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_not_revoke_when_request_is_cancelled()
    {
        var storage = GetStorage();
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromDays(1));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> action = async () =>
            await ((IMessageRevocationStorage)storage).RevokeAsync(message.StorageId, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().NotBeNull();
    }

    public virtual async Task should_have_one_winner_when_revocation_races_first_reservation()
    {
        var storage = GetStorage();
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var message = await _StoreRevocableMessageAsync(TimeSpan.FromDays(1));
            message.InlineAttempts = 1;
            var revoke = Task.Run(
                async () => await ((IMessageRevocationStorage)storage).RevokeAsync(message.StorageId, AbortToken),
                AbortToken
            );
            var reserve = Task.Run(
                async () =>
                    await storage.LeasePublishAndReserveAttemptAsync(message, TimeSpan.FromMinutes(1), 0, AbortToken),
                AbortToken
            );
            await Task.WhenAll(revoke, reserve);
            var result = await revoke;
            var reserved = await reserve;
            (result == MessageRevocationResult.Revoked).Should().Be(!reserved);
            if (reserved)
            {
                result.Should().Be(MessageRevocationResult.AttemptReserved);
            }
        }
    }

    public virtual async Task should_fence_revocation_on_each_persisted_state(
        StatusName status,
        int retries,
        bool nextRetry
    )
    {
        var storage = GetStorage();
        var message = await _StoreRevocableMessageAsync(TimeSpan.FromDays(1));
        message.Retries = retries;
        (
            await storage.ChangePublishStateAsync(
                message,
                status,
                nextRetryAt: nextRetry ? TimeProvider.GetUtcNow().AddMinutes(5) : null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        (await ((IMessageRevocationStorage)storage).RevokeAsync(message.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.AttemptReserved);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().NotBeNull();
    }

    public virtual async Task should_reject_revocation_of_unscheduled_initial_grace_row()
    {
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync(
            "revocation-initial-grace",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        (await ((IMessageRevocationStorage)storage).RevokeAsync(message.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.AttemptReserved);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().NotBeNull();
    }

    public virtual async Task should_have_one_winner_when_revocation_races_claimed_reservation()
    {
        var storage = GetStorage();
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var message = await _StoreRevocableMessageAsync(TimeSpan.FromSeconds(90));
            var claimed = await ((IDelayedMessageClaimStorage)storage).ClaimDelayedMessagesAsync(AbortToken);
            var candidate = claimed.Single(x => x.StorageId == message.StorageId);
            candidate.InlineAttempts = 1;
            var revoke = Task.Run(
                async () => await ((IMessageRevocationStorage)storage).RevokeAsync(candidate.StorageId, AbortToken),
                AbortToken
            );
            var reserve = Task.Run(
                async () => await storage.ReservePublishAttemptAsync(candidate, 0, AbortToken),
                AbortToken
            );
            await Task.WhenAll(revoke, reserve);
            (await revoke)
                .Should()
                .Be(await reserve ? MessageRevocationResult.AttemptReserved : MessageRevocationResult.Revoked);
        }
    }

    private ValueTask<MediumMessage> _StoreRevocableMessageAsync(TimeSpan offset)
    {
        return GetStorage()
            .StoreScheduledMessageAsync(
                "revocation-conformance",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = CreateMessage(),
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                TimeProvider.GetUtcNow().Add(offset),
                cancellationToken: AbortToken
            );
    }
}
