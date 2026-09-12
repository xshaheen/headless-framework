// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    protected abstract (IDataStorage Storage, MessagingOptions Options) CreateSchedulingTestStorage(TimeProvider clock);

    public virtual async Task should_preserve_schedules_and_revocation_across_restart_and_clock_steps()
    {
        var clock = new SchedulingTestClock(DateTimeOffset.UtcNow);
        var (storage, options) = CreateSchedulingTestStorage(clock);
        options.SchedulerBatchSize = 2;
        var due = clock.GetUtcNow().AddMinutes(10).AddTicks(7);
        var handles = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            var stored = await storage.StoreScheduledMessageAsync(
                "scheduling-recovery",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = CreateMessage(),
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                due,
                cancellationToken: AbortToken
            );
            handles.Add(stored.StorageId);
        }

        var version = options.Version;
        options.Version = "other-scheduling-version";
        (await ((IMessageRevocationStorage)storage).RevokeAsync(handles[0], AbortToken))
            .Should()
            .Be(MessageRevocationResult.NotFound);
        options.Version = version;
        (await ((IMessageRevocationStorage)storage).RevokeAsync(handles[0], AbortToken))
            .Should()
            .Be(MessageRevocationResult.Revoked);

        // Relational state survives a new storage instance. InMemory retains only process-local
        // state, so its scheduler recovery uses the same store rather than claiming disk durability.
        if (!SupportsControllableClock)
        {
            (storage, options) = CreateSchedulingTestStorage(clock);
            options.SchedulerBatchSize = 2;
        }

        (await storage.GetMonitoringApi().GetPublishedMessageAsync(handles[0], AbortToken)).Should().BeNull();
        var roundTripped = await storage.GetMonitoringApi().GetPublishedMessageAsync(handles[1], AbortToken);
        roundTripped.Should().NotBeNull();
        roundTripped!.ExpiresAt.Should().BeCloseTo(due, TimeSpan.FromMicroseconds(1));

        clock.UtcNow = clock.GetUtcNow().AddHours(-1);
        var claimer = (IDelayedMessageClaimStorage)storage;
        (await claimer.ClaimDelayedMessagesAsync(AbortToken)).Should().BeEmpty();
        clock.UtcNow = due.AddSeconds(1);
        var claimed = new List<Guid>();
        foreach (var expectedCount in new[] { 2, 2, 1, 0 })
        {
            var batch = await claimer.ClaimDelayedMessagesAsync(AbortToken);
            batch.Should().HaveCount(expectedCount);
            claimed.AddRange(batch.Select(message => message.StorageId));
        }

        claimed.Should().BeEquivalentTo(handles.Skip(1));
        await storage.ChangePublishStateToDelayedAsync([handles[0]], AbortToken);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(handles[0], AbortToken)).Should().BeNull();
    }

    private sealed class SchedulingTestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
