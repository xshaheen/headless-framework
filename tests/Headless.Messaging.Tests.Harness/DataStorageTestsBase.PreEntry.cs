// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    /// <summary>
    /// A transactional attempt that failed before its handler ran hands its reserved attempt back and advances the
    /// row's pre-entry count in one fenced write. The count must survive that write and come back with every received
    /// row the store hands to dispatch, or a fault that never clears would reschedule forever.
    /// </summary>
    public virtual async Task should_keep_the_pre_entry_failure_count_across_reschedule_pickup_and_readmission()
    {
        // given: an admitted inbox row whose first attempt is reserved
        var storage = GetStorage();
        var origin = CreateMessage($"pre-entry-{Guid.NewGuid():N}", "orders.pre-entry");

        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                "orders.pre-entry",
                "orders.pre-entry.consumer",
                "v1",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                cancellationToken: AbortToken
            );

        var admission = await admit();
        admission.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        var message = admission.Message;
        message.PreEntryFailures.Should().Be(0);
        var originalInlineAttempts = message.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                message,
                TimeSpan.FromMinutes(1),
                originalInlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        // when: the attempt failed before the handler ran, so it is rescheduled with the reservation handed back
        message.PreEntryFailures = 1;
        message.InlineAttempts = originalInlineAttempts;
        (
            await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Scheduled,
                MessageContentWrite.Preserve,
                RetryDelay.Exactly(TimeSpan.Zero),
                keepLease: false,
                originalRetries: message.Retries,
                originalInlineAttempts: originalInlineAttempts + 1,
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        // then: the retry claim hands the row back with its count and its unspent attempt
        var claimed = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, null, AbortToken)).Single(
            candidate => candidate.StorageId == message.StorageId
        );
        claimed.PreEntryFailures.Should().Be(1);
        claimed.Retries.Should().Be(0);
        claimed.InlineAttempts.Should().Be(originalInlineAttempts);

        // and: a redelivery's admission reads the same count off the live row
        var duplicate = await admit();
        duplicate.Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
        duplicate.Message.PreEntryFailures.Should().Be(1);
    }
}
