// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Tests.Guarantees;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    [Guarantee("MSG-DASH-03")]
    public virtual async Task should_requeue_only_a_published_row_that_failed_for_good_with_a_fresh_retry_budget()
    {
        // given: a row that failed for good, a row that succeeded, and a row still waiting for a retry
        var storage = GetStorage();
        var failed = await _StorePublishedAsync(storage, "requeue-failed");
        failed.Retries = 2;
        failed.InlineAttempts = 3;
        (
            await storage.ChangePublishStateAsync(
                failed,
                StatusName.Failed,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var succeeded = await _StorePublishedAsync(storage, "requeue-succeeded");
        (
            await storage.ChangePublishStateAsync(
                succeeded,
                StatusName.Succeeded,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var pending = await _StorePublishedAsync(storage, "requeue-pending");
        pending.Retries = 1;
        (
            await storage.ChangePublishStateAsync(
                pending,
                StatusName.Failed,
                retryDelay: RetryDelay.Exactly(TimeSpan.FromHours(1)),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        // when
        var requeued = await storage.RequeuePublishedMessagesAsync(
            [failed.StorageId, succeeded.StorageId, pending.StorageId, Guid.NewGuid()],
            AbortToken
        );

        // then: only the row that failed for good is requeued, with its retry counters reset
        requeued.Should().Equal(failed.StorageId);

        var row = await storage.GetMonitoringApi().GetPublishedMessageAsync(failed.StorageId, AbortToken);
        row.Should().NotBeNull();
        row!.StatusName.Should().Be(StatusName.Failed);
        row.Retries.Should().Be(0);
        row.InlineAttempts.Should().Be(0);
        row.NextRetryAt.Should().NotBeNull();
        row.ExpiresAt.Should().BeNull();
        row.LockedUntil.Should().BeNull();

        var untouched = await storage.GetMonitoringApi().GetPublishedMessageAsync(pending.StorageId, AbortToken);
        untouched!.Retries.Should().Be(1, "a row still waiting for a retry keeps its budget");

        // and: the next retry pickup claims it, and the other rows stay where they were
        var claimed = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        claimed.Select(message => message.StorageId).Should().Equal(failed.StorageId);

        // and: requeuing it again while it waits for its pickup changes nothing
        (await storage.RequeuePublishedMessagesAsync([failed.StorageId], AbortToken))
            .Should()
            .BeEmpty();
    }

    private static async Task<MediumMessage> _StorePublishedAsync(IDataStorage storage, string name)
    {
        return await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);
    }
}
