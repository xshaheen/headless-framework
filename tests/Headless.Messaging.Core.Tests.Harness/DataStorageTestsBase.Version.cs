// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    /// <summary>
    /// <see cref="Headless.Messaging.Configuration.MessagingOptions.Version"/> is only validated for length, so it can
    /// carry SQL metacharacters. Every statement must bind it as data: a quote must neither break the write nor
    /// change which rows the version-scoped reads find.
    /// </summary>
    public virtual async Task should_store_and_find_rows_under_a_version_containing_sql_metacharacters()
    {
        const string version = "v1'; --";
        var (storage, options) = CreateSchedulingTestStorage(TimeProvider);
        options.Version = version;
        var suffix = Guid.NewGuid().ToString("N");

        var published = await storage.StoreMessageAsync(
            $"quoted-version-published-{suffix}",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        var received = await storage.StoreReceivedMessageAsync(
            $"quoted-version-received-{suffix}",
            "quoted-version-group",
            CreateMessage(),
            AbortToken
        );
        var delayed = await storage.StoreScheduledMessageAsync(
            $"quoted-version-delayed-{suffix}",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            _Now().AddSeconds(90),
            cancellationToken: AbortToken
        );

        (await _GetPersistedVersionAsync(storage, MessageType.Publish, $"quoted-version-published-{suffix}"))
            .Should()
            .Be(version);
        (await _GetPersistedVersionAsync(storage, MessageType.Subscribe, $"quoted-version-received-{suffix}"))
            .Should()
            .Be(version);
        (await _GetPersistedVersionAsync(storage, MessageType.Publish, $"quoted-version-delayed-{suffix}"))
            .Should()
            .Be(version);

        await storage.ChangePublishStateAsync(
            published,
            StatusName.Failed,
            retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
            cancellationToken: AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            received,
            StatusName.Failed,
            retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
            cancellationToken: AbortToken
        );

        // Another version must not see the rows, so the reads below are scoped by the bound value.
        options.Version = "v1";
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, null, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
        options.Version = version;

        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == published.StorageId);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, null, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == received.StorageId);

        var scheduled = new List<MediumMessage>();
        await storage.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduled.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );
        scheduled.Should().ContainSingle(m => m.StorageId == delayed.StorageId);

        if (storage is IDelayedMessageClaimStorage claimStorage)
        {
            (await claimStorage.ClaimDelayedMessagesAsync(AbortToken))
                .Should()
                .ContainSingle(m => m.StorageId == delayed.StorageId);
        }
    }

    private async Task<string> _GetPersistedVersionAsync(IDataStorage storage, MessageType type, string name)
    {
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = type,
                    Name = name,
                    PageSize = 20,
                },
                AbortToken
            );

        return page.Items.Should().ContainSingle().Which.Version;
    }
}
