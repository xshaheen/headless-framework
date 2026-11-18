// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    private const string _NamedTransport = "partner";

    /// <summary>
    /// A published row stores the named transport it was published through, so every read that hands the row to the
    /// relay, and the dashboard's reads, give it back: the relay must send it through the same broker after a restart.
    /// </summary>
    [Fact]
    public async Task should_round_trip_the_named_transport_of_a_published_row_through_every_read()
    {
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring roundtrip");
        }

        // given
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"named-transport-{Guid.NewGuid():N}";
        var named = await storage.StoreMessageAsync(
            name,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
                TransportName = _NamedTransport,
            },
            cancellationToken: AbortToken
        );
        var unnamed = await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);

        // when
        var namedRow = await monitoring.GetPublishedMessageAsync(named.StorageId, AbortToken);
        var unnamedRow = await monitoring.GetPublishedMessageAsync(unnamed.StorageId, AbortToken);
        var listed = await monitoring.ListMessagesAsync(
            new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            after: null,
            pageSize: 50,
            AbortToken
        );

        await storage.ChangePublishStateAsync(
            named,
            StatusName.Failed,
            retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
            cancellationToken: AbortToken
        );
        var retried = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);

        // then
        named.TransportName.Should().Be(_NamedTransport);
        namedRow!.TransportName.Should().Be(_NamedTransport);
        unnamedRow!.TransportName.Should().BeNull("a row published through the default transport names none");
        listed.Items.Single(row => row.StorageId == named.StorageId).TransportName.Should().Be(_NamedTransport);
        listed.Items.Single(row => row.StorageId == unnamed.StorageId).TransportName.Should().BeNull();
        retried
            .Should()
            .ContainSingle(row => row.StorageId == named.StorageId)
            .Which.TransportName.Should()
            .Be(_NamedTransport);
    }

    /// <summary>A delayed published row keeps its named transport through the claim that hands it to the relay.</summary>
    [Fact]
    public async Task should_keep_the_named_transport_of_a_delayed_row_through_its_claim()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        // given
        var dueAt = TimeProvider.GetUtcNow().AddSeconds(20);
        var stored = await storage.StoreMessageAsync(
            "named-transport-delayed",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
                ExpiresAt = dueAt,
                TransportName = _NamedTransport,
            },
            cancellationToken: AbortToken
        );
        stored.ExpiresAt = dueAt;
        (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        // when
        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);

        // then
        claimed
            .Should()
            .ContainSingle(row => row.StorageId == stored.StorageId)
            .Which.TransportName.Should()
            .Be(_NamedTransport);
    }
}
