// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Module;

/// <summary>
/// The Messaging module's history reads: the hourly series of every store, the inbox generations of one received
/// message, the consumers of the host manifest with their implementation, each store's backlog, and the providers.
/// </summary>
public sealed class MessagingHistoryApiTests : TestBase
{
    private const string _Outbox = "billing";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_serve_24_hours_per_store_and_a_null_named_series_for_a_store_that_fails()
    {
        // given: the main store holds one succeeded published row and one failed received row this hour, and the
        // additional outbox fails every read
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
                services.AddSingleton(provider =>
                    _Stores(provider.GetRequiredService<IDataStorage>(), _FailingOutbox())
                )
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        await MessagingRows.PublishedAsync(storage, "order-1", StatusName.Succeeded, AbortToken);
        await MessagingRows.ReceivedFailureAsync(storage, "order-2", cancellationToken: AbortToken);
        using var client = app.GetTestClient();

        // when
        var history = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/metrics/hourly",
            AbortToken
        );

        // then: 24 hours ending with the current one, in UTC; the main store is counted, the outbox is null and named
        history.GetProperty("interval").GetString().Should().Be("PT1H");
        history.GetProperty("zone").GetString().Should().Be("UTC");
        history.GetProperty("asOf").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
        var hours = history.GetProperty("hourStarts");
        hours.GetArrayLength().Should().Be(24);
        hours[23].GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        hours[0].GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 7, 13, 0, 0, TimeSpan.Zero));

        var stores = history.GetProperty("stores");
        stores.GetArrayLength().Should().Be(2);
        var primary = stores[0];
        primary.GetProperty("store").GetString().Should().Be("primary");
        _Series(primary.GetProperty("published"), "succeeded")[23].Should().Be(1);
        _Series(primary.GetProperty("published"), "succeeded")[..23].Should().OnlyContain(static count => count == 0);
        _Series(primary.GetProperty("published"), "failed").Should().OnlyContain(static count => count == 0);
        _Series(primary.GetProperty("received"), "failed")[23].Should().Be(1);
        _Series(primary.GetProperty("received"), "succeeded")[23].Should().Be(0);

        var outbox = stores[1];
        outbox.GetProperty("store").GetString().Should().Be(_Outbox);
        outbox.TryGetProperty("published", out _).Should().BeFalse("an unread store has no series, never zeros");
        outbox.TryGetProperty("received", out _).Should().BeFalse();
        history
            .GetProperty("unavailable")
            .EnumerateArray()
            .Select(static store => store.GetString())
            .Should()
            .Equal(_Outbox);
    }

    [Fact]
    public async Task should_serve_the_original_and_its_replay_with_the_replays_parent_when_a_message_was_replayed()
    {
        // given: a failed inbox generation, re-executed by an operator a minute later
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var original = await MessagingCommandHost.InboxFailureAsync(storage, "order-1", AbortToken);
        _time.Advance(TimeSpan.FromMinutes(1));
        var replay = await storage
            .GetInboxOperationsApi()
            .ForceReprocessAsync(
                new InboxOperationRequest(
                    Guid.NewGuid(),
                    original.InboxGeneration!.IncarnationId,
                    StatusName.Failed,
                    "incident 42",
                    new OperatorAuthorizationContext(
                        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ops")], "test"))
                    )
                ),
                AbortToken
            );
        replay.Outcome.Should().Be(InboxOperationOutcome.Applied);
        using var client = app.GetTestClient();

        // when
        var list = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/received/generations?messageId=order-1",
            AbortToken
        );

        // then: oldest first, each row with its generation; the replay names the row it re-executed
        var items = list.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        var first = items[0];
        first.GetProperty("storageId").GetGuid().Should().Be(original.StorageId);
        var firstGeneration = first.GetProperty("generation");
        firstGeneration.GetProperty("number").GetInt64().Should().Be(original.InboxGeneration.Number);
        firstGeneration.GetProperty("incarnationId").GetGuid().Should().Be(original.InboxGeneration.IncarnationId);
        firstGeneration.GetProperty("origin").GetString().Should().Be("original");
        firstGeneration.GetProperty("isCurrent").GetBoolean().Should().BeFalse();
        firstGeneration.GetProperty("recovery").GetString().Should().Be("routable");
        firstGeneration.TryGetProperty("replayOfId", out _).Should().BeFalse();
        firstGeneration.TryGetProperty("guarantee", out _).Should().BeFalse();

        var second = items[1];
        second.GetProperty("storageId").GetGuid().Should().Be(replay.ChildStorageId!.Value);
        var secondGeneration = second.GetProperty("generation");
        secondGeneration.GetProperty("number").GetInt64().Should().Be(replay.ChildGeneration!.Value);
        secondGeneration.GetProperty("origin").GetString().Should().Be("replay");
        secondGeneration.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        secondGeneration
            .GetProperty("replayOfIncarnationId")
            .GetGuid()
            .Should()
            .Be(original.InboxGeneration.IncarnationId);
        secondGeneration.GetProperty("replayOfId").GetString().Should().Be(first.GetProperty("id").GetString());
    }

    [Fact]
    public async Task should_serve_rows_without_a_generation_when_the_store_has_no_inbox_operations()
    {
        // given: a store that lists received rows but cannot read inbox generations
        var row = new MessageView
        {
            StorageId = Guid.NewGuid(),
            MessageId = "order-1",
            Version = "v1",
            Name = MessagingRows.Name,
            ConsumerIdentity = MessagingRows.Consumer,
            StatusName = StatusName.Failed,
            Added = _time.GetUtcNow(),
        };
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .ListMessagesAsync(
                Arg.Any<MessageListQuery>(),
                Arg.Any<MessageListPosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new MessageListPage { Items = [row] });
        var store = _QuietStore();
        store.GetMonitoringApi().Returns(monitoring);
        store.GetInboxOperationsApi().Throws(new NotSupportedException("No inbox operations here."));
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
                services.AddSingleton(
                    new MessagingStores("node-a", [new("primary", store, HoldsReceived: true)], TimeSpan.FromSeconds(5))
                )
        );
        using var client = app.GetTestClient();

        // when
        var list = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/received/generations?messageId=order-1",
            AbortToken
        );

        // then
        var item = list.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("storageId").GetGuid().Should().Be(row.StorageId);
        item.TryGetProperty("generation", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_serve_each_consumers_implementation_type_and_handler_method()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            messaging: setup => setup.AddModule<OrdersModule>()
        );
        using var client = app.GetTestClient();

        // when
        var consumers = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/consumers",
            AbortToken
        );

        // then
        var consumer = consumers
            .GetProperty("items")
            .EnumerateArray()
            .Should()
            .ContainSingle(static item => item.GetProperty("identity").GetString() == OrdersModule.Identity)
            .Subject;
        var message = consumer.GetProperty("messages")[0];
        message.GetProperty("consumerType").GetString().Should().Be(typeof(OrderPlacedConsumer).FullName);
        message.GetProperty("handlerMethod").GetString().Should().Be("ConsumeAsync");
    }

    [Fact]
    public async Task should_serve_each_stores_backlog_and_name_a_store_that_fails()
    {
        // given: the main store holds one published and one received row still waiting, and the additional outbox
        // fails every read. The clock stays put: moving it would fire the host's own dispatch of the waiting rows.
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
                services.AddSingleton(provider =>
                    _Stores(provider.GetRequiredService<IDataStorage>(), _FailingOutbox())
                )
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        await MessagingRows.PublishedAsync(storage, "order-1", StatusName.Scheduled, AbortToken);
        await storage.StoreReceivedMessageAsync(
            MessagingRows.Name,
            MessagingRows.Consumer,
            MessagingRows.Medium("order-2"),
            AbortToken
        );
        using var client = app.GetTestClient();

        // when
        var backlog = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/backlog",
            AbortToken
        );

        // then: the main store is counted with the age of its oldest row; the outbox is named, never shown as empty
        backlog.GetProperty("asOf").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
        var primary = backlog.GetProperty("stores").EnumerateArray().Should().ContainSingle().Subject;
        primary.GetProperty("store").GetString().Should().Be("primary");
        primary.GetProperty("publishedPending").GetInt64().Should().Be(1);
        primary.GetProperty("oldestPublishedSeconds").GetDouble().Should().Be(0);
        primary.GetProperty("receivedPending").GetInt64().Should().Be(1);
        primary.GetProperty("oldestReceivedSeconds").GetDouble().Should().Be(0);
        backlog
            .GetProperty("unavailable")
            .EnumerateArray()
            .Select(static store => store.GetString())
            .Should()
            .Equal(_Outbox);
    }

    [Fact]
    public async Task should_serve_no_received_backlog_for_an_additional_outbox()
    {
        // given: an additional outbox with four published rows waiting, the oldest for 90 seconds
        var outbox = _QuietStore();
        outbox
            .GetMonitoringApi()
            .GetBacklogAsync(Arg.Any<CancellationToken>())
            .Returns(
                new BacklogView
                {
                    PublishedPending = 4,
                    ReceivedPending = 0,
                    OldestPublishedPendingAge = TimeSpan.FromSeconds(90),
                }
            );
        await using var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services =>
                services.AddSingleton(provider => _Stores(provider.GetRequiredService<IDataStorage>(), outbox))
        );
        using var client = app.GetTestClient();

        // when
        var backlog = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/backlog",
            AbortToken
        );

        // then: an outbox holds no received rows, so it serves no received figures; an empty main store has no oldest
        var stores = backlog.GetProperty("stores");
        stores.GetArrayLength().Should().Be(2);
        stores[0].GetProperty("publishedPending").GetInt64().Should().Be(0);
        stores[0].GetProperty("receivedPending").GetInt64().Should().Be(0);
        stores[0].TryGetProperty("oldestPublishedSeconds", out _).Should().BeFalse();
        stores[1].GetProperty("store").GetString().Should().Be(_Outbox);
        stores[1].GetProperty("publishedPending").GetInt64().Should().Be(4);
        stores[1].GetProperty("oldestPublishedSeconds").GetDouble().Should().Be(90);
        stores[1].TryGetProperty("receivedPending", out _).Should().BeFalse();
        stores[1].TryGetProperty("oldestReceivedSeconds", out _).Should().BeFalse();
        backlog.GetProperty("unavailable").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task should_serve_the_broker_of_each_lane_and_the_storage_of_each_outbox()
    {
        // given: a host on the in-memory transport and the in-memory store
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        using var client = app.GetTestClient();

        // when
        var providers = await MessagingModuleTestHost.GetJsonAsync(
            client,
            MessagingModuleTestHost.Api + "/providers",
            AbortToken
        );

        // then: each lane names its broker, and the main outbox its storage provider, by name only
        var transports = providers.GetProperty("transports");
        transports.GetProperty("bus").GetString().Should().Be("in_memory");
        transports.GetProperty("queue").GetString().Should().Be("in_memory");
        var outbox = providers.GetProperty("outboxes").EnumerateArray().Should().ContainSingle().Subject;
        outbox.GetProperty("name").GetString().Should().Be("primary");
        outbox.GetProperty("storage").GetString().Should().NotBeNullOrEmpty();
    }

    private static int?[] _Series(System.Text.Json.JsonElement series, string status) =>
        [
            .. series
                .GetProperty(status)
                .EnumerateArray()
                .Select(static count =>
                    count.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : count.GetInt32()
                ),
        ];

    private static MessagingStores _Stores(IDataStorage primary, IDataStorage outbox) =>
        new(
            "node-a",
            [new("primary", primary, HoldsReceived: true), new(_Outbox, outbox, HoldsReceived: false)],
            TimeSpan.FromMilliseconds(200)
        );

    private static IDataStorage _FailingOutbox()
    {
        var outbox = _QuietStore();
        outbox.GetMonitoringApi().Throws(new InvalidOperationException("billing is down"));

        return outbox;
    }

    // The change publisher polls every store in the background; a store that refuses those reads stays out of it.
    private static IDataStorage _QuietStore()
    {
        var store = Substitute.For<IDataStorage>();
        store
            .GetMessageChangeStartCursorAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store is down"));
        store
            .GetMessageChangesAsync(
                Arg.Any<MessageChangeCursor>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("store is down"));

        return store;
    }

    public sealed record OrderPlaced(string OrderId);

    public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class OrdersModule : IMessagingModule
    {
        public const string Identity = "orders.order-placed-consumer";

        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<OrderPlacedConsumer, OrderPlaced>(
                Identity,
                everyInstance: false,
                static (_, _, _) => ValueTask.CompletedTask
            );
        }
    }
}
