// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Testing.Tests;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection("InMemoryStorage")]
public sealed class InMemoryMonitoringApiTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ISerializer _serializer = Substitute.For<ISerializer>();
    private readonly ILongIdGenerator _idGenerator = Substitute.For<ILongIdGenerator>();
    private readonly IOptions<MessagingOptions> _options = Options.Create(new MessagingOptions { FailedRetryCount = 3 });
    private readonly InMemoryDataStorage _storage;
    private readonly InMemoryMonitoringApi _sut;
    private long _idCounter = 1000;

    public InMemoryMonitoringApiTests()
    {
        // Clear static state before each test
        InMemoryDataStorage.PublishedMessages.Clear();
        InMemoryDataStorage.ReceivedMessages.Clear();

        _timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
        _serializer.Serialize(Arg.Any<Message>()).Returns(call => JsonSerializer.Serialize(call.Arg<Message>()));
        _idGenerator.Create().Returns(_ => Interlocked.Increment(ref _idCounter));

        _storage = new InMemoryDataStorage(_options, _serializer, _idGenerator, _timeProvider);
        _sut = new InMemoryMonitoringApi(_timeProvider);
    }

    [Fact]
    public async Task should_get_published_message_by_id()
    {
        // given
        var message = _CreateMessage("123");
        await _storage.StoreMessageAsync("test.topic", message, cancellationToken: AbortToken);

        // when
        var result = await _sut.GetPublishedMessageAsync(123, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.DbId.Should().Be("123");
    }

    [Fact]
    public async Task should_return_null_when_published_message_not_found()
    {
        // when
        var result = await _sut.GetPublishedMessageAsync(999999, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_get_received_message_by_id()
    {
        // given
        var message = _CreateMessage("recv-456");
        var stored = await _storage.StoreReceivedMessageAsync("test.topic", "group", message, AbortToken);
        var id = long.Parse(stored.DbId, CultureInfo.InvariantCulture);

        // when
        var result = await _sut.GetReceivedMessageAsync(id, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.DbId.Should().Be(stored.DbId);
    }

    [Fact]
    public async Task should_return_null_when_received_message_not_found()
    {
        // when
        var result = await _sut.GetReceivedMessageAsync(999999, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_statistics()
    {
        // given
        var pubSucceeded = await _storage.StoreMessageAsync("topic", _CreateMessage("ps-1"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(pubSucceeded, StatusName.Succeeded, cancellationToken: AbortToken);

        var pubFailed = await _storage.StoreMessageAsync("topic", _CreateMessage("pf-1"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(pubFailed, StatusName.Failed, cancellationToken: AbortToken);

        var pubDelayed = await _storage.StoreMessageAsync("topic", _CreateMessage("pd-1"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(pubDelayed, StatusName.Delayed, cancellationToken: AbortToken);

        var recvSucceeded = await _storage.StoreReceivedMessageAsync("topic", "group", _CreateMessage("rs-1"), AbortToken);
        await _storage.ChangeReceiveStateAsync(recvSucceeded, StatusName.Succeeded, AbortToken);

        var recvFailed = await _storage.StoreReceivedMessageAsync("topic", "group", _CreateMessage("rf-1"), AbortToken);
        await _storage.ChangeReceiveStateAsync(recvFailed, StatusName.Failed, AbortToken);

        // when
        var stats = await _sut.GetStatisticsAsync(AbortToken);

        // then
        stats.PublishedSucceeded.Should().Be(1);
        stats.PublishedFailed.Should().Be(1);
        stats.PublishedDelayed.Should().Be(1);
        stats.ReceivedSucceeded.Should().Be(1);
        stats.ReceivedFailed.Should().Be(1);
    }

    [Fact]
    public async Task should_return_published_failed_count()
    {
        // given
        var msg1 = await _storage.StoreMessageAsync("topic", _CreateMessage("pfc-1"), cancellationToken: AbortToken);
        var msg2 = await _storage.StoreMessageAsync("topic", _CreateMessage("pfc-2"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg1, StatusName.Failed, cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg2, StatusName.Failed, cancellationToken: AbortToken);

        // when
        var count = await _sut.PublishedFailedCount(AbortToken);

        // then
        count.Should().Be(2);
    }

    [Fact]
    public async Task should_return_published_succeeded_count()
    {
        // given
        var msg = await _storage.StoreMessageAsync("topic", _CreateMessage("psc-1"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg, StatusName.Succeeded, cancellationToken: AbortToken);

        // when
        var count = await _sut.PublishedSucceededCount(AbortToken);

        // then
        count.Should().Be(1);
    }

    [Fact]
    public async Task should_return_received_failed_count()
    {
        // given
        var msg = await _storage.StoreReceivedMessageAsync("topic", "group", _CreateMessage("rfc-1"), AbortToken);
        await _storage.ChangeReceiveStateAsync(msg, StatusName.Failed, AbortToken);

        // when
        var count = await _sut.ReceivedFailedCount(AbortToken);

        // then
        count.Should().Be(1);
    }

    [Fact]
    public async Task should_return_received_succeeded_count()
    {
        // given
        var msg = await _storage.StoreReceivedMessageAsync("topic", "group", _CreateMessage("rsc-1"), AbortToken);
        await _storage.ChangeReceiveStateAsync(msg, StatusName.Succeeded, AbortToken);

        // when
        var count = await _sut.ReceivedSucceededCount(AbortToken);

        // then
        count.Should().Be(1);
    }

    [Fact]
    public async Task should_query_published_messages()
    {
        // given
        var msg1 = await _storage.StoreMessageAsync("orders.created", _CreateMessage("qp-1"), cancellationToken: AbortToken);
        var msg2 = await _storage.StoreMessageAsync("users.created", _CreateMessage("qp-2"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg1, StatusName.Succeeded, cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg2, StatusName.Failed, cancellationToken: AbortToken);

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            StatusName = "Succeeded",
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be("qp-1");
        result.Items[0].StatusName.Should().Be("Succeeded");
    }

    [Fact]
    public async Task should_query_published_messages_by_name()
    {
        // given
        await _storage.StoreMessageAsync("orders.created", _CreateMessage("qn-1"), cancellationToken: AbortToken);
        await _storage.StoreMessageAsync("users.created", _CreateMessage("qn-2"), cancellationToken: AbortToken);

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            Name = "orders.created",
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("orders.created");
    }

    [Fact]
    public async Task should_query_published_messages_by_content()
    {
        // given
        var msg1 = _CreateMessage("qc-1");
        var msg2 = _CreateMessage("qc-2");

        _serializer.Serialize(Arg.Is<Message>(m => m == msg1)).Returns("{\"searchterm\":\"findme\"}");
        _serializer.Serialize(Arg.Is<Message>(m => m == msg2)).Returns("{\"data\":\"other\"}");

        await _storage.StoreMessageAsync("topic", msg1, cancellationToken: AbortToken);
        await _storage.StoreMessageAsync("topic", msg2, cancellationToken: AbortToken);

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            Content = "findme",
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be("qc-1");
    }

    [Fact]
    public async Task should_query_received_messages()
    {
        // given
        await _storage.StoreReceivedMessageAsync("topic1", "group1", _CreateMessage("qr-1"), AbortToken);
        await _storage.StoreReceivedMessageAsync("topic2", "group2", _CreateMessage("qr-2"), AbortToken);

        var query = new MessageQuery
        {
            MessageType = MessageType.Subscribe,
            Group = "group1",
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().ContainSingle();
        result.Items[0].Group.Should().Be("group1");
    }

    [Fact]
    public async Task should_support_pagination_for_published_messages()
    {
        // given
        for (var i = 0; i < 25; i++)
        {
            await _storage.StoreMessageAsync("topic", _CreateMessage($"page-{i}"), cancellationToken: AbortToken);
        }

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            CurrentPage = 1,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().HaveCount(10);
        result.TotalItems.Should().Be(25);
        result.Index.Should().Be(1);
        result.Size.Should().Be(10);
    }

    [Fact]
    public async Task should_support_pagination_for_received_messages()
    {
        // given
        for (var i = 0; i < 15; i++)
        {
            await _storage.StoreReceivedMessageAsync("topic", "group", _CreateMessage($"rpage-{i}"), AbortToken);
        }

        var query = new MessageQuery
        {
            MessageType = MessageType.Subscribe,
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().HaveCount(10);
        result.TotalItems.Should().Be(15);
    }

    [Fact]
    public async Task should_return_hourly_failed_jobs_for_published()
    {
        // given
        var msg = await _storage.StoreMessageAsync("topic", _CreateMessage("hf-1"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg, StatusName.Failed, cancellationToken: AbortToken);

        // when
        var result = await _sut.HourlyFailedJobs(MessageType.Publish, AbortToken);

        // then
        result.Should().NotBeEmpty();
        result.Should().HaveCount(24);
    }

    [Fact]
    public async Task should_return_hourly_succeeded_jobs_for_received()
    {
        // given
        var msg = await _storage.StoreReceivedMessageAsync("topic", "group", _CreateMessage("hs-1"), AbortToken);
        await _storage.ChangeReceiveStateAsync(msg, StatusName.Succeeded, AbortToken);

        // when
        var result = await _sut.HourlySucceededJobs(MessageType.Subscribe, AbortToken);

        // then
        result.Should().NotBeEmpty();
        result.Should().HaveCount(24);
    }

    [Fact]
    public async Task should_return_message_view_with_all_properties()
    {
        // given
        var message = _CreateMessage("view-test");
        await _storage.StoreMessageAsync("test.topic.name", message, cancellationToken: AbortToken);
        InMemoryDataStorage.PublishedMessages["view-test"].ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1);
        InMemoryDataStorage.PublishedMessages["view-test"].Retries = 3;

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        var view = result.Items[0];
        view.Id.Should().Be("view-test");
        view.Name.Should().Be("test.topic.name");
        view.Version.Should().Be("N/A");
        view.Retries.Should().Be(3);
        view.ExpiresAt.Should().NotBeNull();
        view.Added.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task should_return_empty_page_when_no_messages()
    {
        // given
        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task should_filter_by_status_case_insensitive()
    {
        // given
        var msg = await _storage.StoreMessageAsync("topic", _CreateMessage("case-test"), cancellationToken: AbortToken);
        await _storage.ChangePublishStateAsync(msg, StatusName.Succeeded, cancellationToken: AbortToken);

        var query = new MessageQuery
        {
            MessageType = MessageType.Publish,
            StatusName = "succeeded", // lowercase
            CurrentPage = 0,
            PageSize = 10,
        };

        // when
        var result = await _sut.GetMessagesAsync(query, AbortToken);

        // then
        result.Items.Should().ContainSingle();
    }

    private static Message _CreateMessage(string id)
    {
        return new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal) { { Headers.MessageId, id } },
            new { Data = "test" }
        );
    }
}
