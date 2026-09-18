// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using Headless.Dashboard.Authentication;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class PublishedMessageEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();
    private readonly IScheduledDeliveryOperationsApi _scheduledOperationsApi =
        Substitute.For<IScheduledDeliveryOperationsApi>();

    public PublishedMessageEndpointTests()
    {
        // Legacy bulk requeue/delete fence pending scheduled ids (KTD10); default to no pending rows so
        // existing published-message behavior is unaffected unless a test configures otherwise.
        _scheduledOperationsApi
            .QueryAsync(
                Arg.Any<ScheduledDeliveryQuery>(),
                Arg.Any<OperatorAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(new IndexPage<ScheduledDeliveryView>([], 0, 200, 0)));
        _dataStorage.GetScheduledDeliveryOperationsApi().Returns(_scheduledOperationsApi);
    }

    [Fact]
    public async Task should_return_message_content_when_published_message_details()
    {
        // given
        var messageId = Guid.Parse("11111111-1111-1111-1111-111111111123");
        var message = new MediumMessage
        {
            StorageId = messageId,
            Content = "{\"key\":\"value\"}",
            Lane = MessageLane.Bus,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "logical-pub-123",
                    [Headers.MessageName] = "orders.created",
                },
                new { Data = "test" }
            ),
        };

        _monitoringApi
            .GetPublishedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(message));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/published/message/{messageId}", AbortToken);

        // then
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(AbortToken);
        payload.Should().ContainKey("storageId");
        payload.Should().ContainKey("messageId");
        payload.Should().ContainKey("lane");
        payload["lane"].GetString().Should().Be(nameof(MessageLane.Bus));
        payload["requestedDeliveryMode"].ValueKind.Should().Be(JsonValueKind.Null);
        payload["resolvedDeliveryMode"].GetString().Should().Be(nameof(DeliveryMode.Durable));
        payload["requestedEnlistment"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_return_404_for_missing_message_when_published_message_details()
    {
        // given
        var messageId = Guid.Parse("11111111-1111-1111-1111-111111111999");
        _monitoringApi
            .GetPublishedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(null));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/published/message/{messageId}", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_bind_lane_filter_and_project_delivery_metadata_with_pagination_when_published_list()
    {
        // given
        var result = new IndexPage<MessageView>(
            [
                new MessageView
                {
                    StorageId = Guid.Parse("11111111-1111-1111-1111-111111111123"),
                    MessageId = "logical-pub-123",
                    Version = "v1",
                    Name = "orders.created",
                    Lane = MessageLane.Queue,
                    RequestedDeliveryMode = DeliveryMode.Direct,
                    ResolvedDeliveryMode = DeliveryMode.Durable,
                    RequestedEnlistment = Headless.UnitOfWork.TransactionEnlistment.Required,
                    Content = "{\"key\":\"value\"}",
                    Added = new DateTimeOffset(2026, 03, 24, 10, 00, 00, TimeSpan.Zero),
                    Retries = 2,
                    StatusName = StatusName.Succeeded,
                },
            ],
            index: 1,
            size: 20,
            totalItems: 35
        );

        _monitoringApi
            .GetMessagesAsync(Arg.Any<MessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync(
            "/api/published/Succeeded?currentPage=2&perPage=20&lane=Queue",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(
            cancellationToken: AbortToken
        );

        payload
            .Should()
            .ContainKeys("items", "index", "size", "totalItems", "totalPages", "hasPrevious", "hasNext", "totals");

        payload["index"].GetInt32().Should().Be(1);
        payload["size"].GetInt32().Should().Be(20);
        payload["totalItems"].GetInt32().Should().Be(35);
        payload["totals"].GetInt32().Should().Be(35);

        var item = payload["items"].EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("storageId").GetString().Should().Be("11111111-1111-1111-1111-111111111123");
        item.GetProperty("messageId").GetString().Should().Be("logical-pub-123");
        item.GetProperty("lane").GetString().Should().Be(nameof(MessageLane.Queue));
        item.GetProperty("requestedDeliveryMode").GetString().Should().Be(nameof(DeliveryMode.Direct));
        item.GetProperty("resolvedDeliveryMode").GetString().Should().Be(nameof(DeliveryMode.Durable));
        item.GetProperty("requestedEnlistment")
            .GetString()
            .Should()
            .Be(nameof(Headless.UnitOfWork.TransactionEnlistment.Required));
        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Publish
                    && query.StatusName == StatusName.Succeeded
                    && query.Lane == MessageLane.Queue
                    && query.CurrentPage == 1
                    && query.PageSize == 20
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bind_null_intent_filter_when_published_list_intent_type_is_omitted()
    {
        // given
        var result = new IndexPage<MessageView>([], index: 1, size: 20, totalItems: 0);

        _monitoringApi
            .GetMessagesAsync(Arg.Any<MessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when — lane omitted from query string
        var response = await client.GetAsync("/api/published/Succeeded?currentPage=2&perPage=20", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Publish
                    && query.StatusName == StatusName.Succeeded
                    && query.Lane == null
                    && query.CurrentPage == 1
                    && query.PageSize == 20
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_422_for_empty_array_when_published_requeue()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/published/requeue", Array.Empty<long>(), AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task should_return_422_for_null_body_when_published_delete()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        using var stringContent = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/published/delete", stringContent, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task should_return_204_on_success_when_published_delete()
    {
        // given
        var messageId = Guid.Parse("11111111-1111-1111-1111-111111111123");
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage
            .DeletePublishedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(1));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/published/delete", new[] { messageId }, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_reject_pending_scheduled_id_and_delete_others_when_published_delete()
    {
        // given: one pending scheduled id (rejected, KTD10) and one Succeeded id (deleted, unaffected).
        var pendingId = Guid.Parse("11111111-1111-1111-1111-111111111991");
        var succeededId = Guid.Parse("11111111-1111-1111-1111-111111111992");
        _scheduledOperationsApi
            .QueryAsync(
                Arg.Any<ScheduledDeliveryQuery>(),
                Arg.Any<OperatorAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                ValueTask.FromResult(
                    new IndexPage<ScheduledDeliveryView>(
                        [
                            new ScheduledDeliveryView(
                                pendingId,
                                "msg-991",
                                "orders.created",
                                MessageLane.Bus,
                                DateTimeOffset.UtcNow.AddMinutes(5),
                                "Pending",
                                false,
                                null,
                                null,
                                0
                            ),
                        ],
                        0,
                        200,
                        1
                    )
                )
            );
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage
            .DeletePublishedMessagesAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == succeededId),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(1));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync(
            "/api/published/delete",
            new[] { pendingId, succeededId },
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("rejected")[0].GetString().Should().Be(pendingId.ToString());
        document.RootElement.GetProperty("deleted").GetInt32().Should().Be(1);
        await _dataStorage
            .Received(1)
            .DeletePublishedMessagesAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == succeededId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_reject_pending_scheduled_id_and_requeue_others_when_published_requeue()
    {
        // given: one pending scheduled id (rejected, KTD10) and one Succeeded id (requeued, unaffected).
        var pendingId = Guid.Parse("11111111-1111-1111-1111-111111111991");
        var succeededId = Guid.Parse("11111111-1111-1111-1111-111111111992");
        _scheduledOperationsApi
            .QueryAsync(
                Arg.Any<ScheduledDeliveryQuery>(),
                Arg.Any<OperatorAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                ValueTask.FromResult(
                    new IndexPage<ScheduledDeliveryView>(
                        [
                            new ScheduledDeliveryView(
                                pendingId,
                                "msg-991",
                                "orders.created",
                                MessageLane.Bus,
                                DateTimeOffset.UtcNow.AddMinutes(5),
                                "Pending",
                                false,
                                null,
                                null,
                                0
                            ),
                        ],
                        0,
                        200,
                        1
                    )
                )
            );
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _monitoringApi
            .GetPublishedMessagesAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == succeededId),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                ValueTask.FromResult<IReadOnlyList<MediumMessage>>([
                    new MediumMessage
                    {
                        StorageId = succeededId,
                        Lane = MessageLane.Bus,
                        Content = "{}",
                        Origin = new Message(
                            new Dictionary<string, string?>(StringComparer.Ordinal)
                            {
                                [Headers.MessageId] = "msg-992",
                                [Headers.MessageName] = "orders.created",
                            },
                            new { }
                        ),
                    },
                ])
            );

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync(
            "/api/published/requeue",
            new[] { pendingId, succeededId },
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("rejected")[0].GetString().Should().Be(pendingId.ToString());
        document.RootElement.GetProperty("requeued")[0].GetString().Should().Be(succeededId.ToString());
        document.RootElement.GetProperty("message").GetString().Should().Contain("scheduled-delivery");
    }

    private static WebApplication _CreateTestApp(IDataStorage dataStorage)
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();

        var appBuilder = WebApplication.CreateSlimBuilder();
        appBuilder.WebHost.UseTestServer();

        appBuilder.Services.AddSingleton(config);
        appBuilder.Services.AddSingleton(config.Auth);
        appBuilder.Services.AddScoped<IAuthService, AuthService>();
        appBuilder.Services.AddSingleton(dataStorage);
        appBuilder.Services.AddSingleton<MessagingMetricsEventListener>();

        // Gateway proxy deps for ActivatorUtilities resolution
        appBuilder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        appBuilder.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        appBuilder.Services.AddSingleton<MessagingDashboardCache>();
        appBuilder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        appBuilder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        appBuilder.Services.AddSingleton<GatewayProxyAgent>();

        // Legacy requeue's transport-availability check (KTD10 fencing tests exercise the happy path).
        appBuilder.Services.AddSingleton(Substitute.For<IDispatcher>());
        appBuilder.Services.AddSingleton(Substitute.For<IBusTransport>());

        appBuilder.Services.AddRouting();
        appBuilder.Services.AddAuthorization();
        appBuilder.Services.AddCors(o => o.AddPolicy("HeadlessMessagingDashboardCORS", p => p.AllowAnyOrigin()));

        var app = appBuilder.Build();
        app.UseRouting();
        app.UseCors("HeadlessMessagingDashboardCORS");
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }
}
