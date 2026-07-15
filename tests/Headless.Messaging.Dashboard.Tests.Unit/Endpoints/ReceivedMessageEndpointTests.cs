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
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class ReceivedMessageEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task should_return_message_content_when_received_message_details()
    {
        // given
        var messageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x14, 0x56);
        var message = new MediumMessage
        {
            StorageId = messageId,
            Content = "{\"received\":\"data\"}",
            IntentType = IntentType.Bus,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = "logical-rec-456",
                    [Headers.MessageName] = "orders.received",
                    [Headers.Group] = "workers",
                },
                new { Data = "received" }
            ),
        };

        _monitoringApi
            .GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(message));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/received/message/{messageId}", AbortToken);

        // then
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>(
            cancellationToken: AbortToken
        );
        payload.Should().ContainKey("storageId");
        payload.Should().ContainKey("messageId");
        payload.Should().ContainKey("intentType");
        ((JsonElement)payload["intentType"]!).GetInt32().Should().Be((int)IntentType.Bus);
    }

    [Fact]
    public async Task should_return_404_for_missing_message_when_received_message_details()
    {
        // given
        var messageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x18, 0x88);
        _monitoringApi
            .GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(null));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/received/message/{messageId}", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_bind_intent_filter_and_project_intent_with_pagination_metadata_when_received_list()
    {
        // given
        var result = new IndexPage<MessageView>(
            [
                new MessageView
                {
                    StorageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x14, 0x56),
                    MessageId = "logical-rec-456",
                    Version = "v1",
                    Name = "orders.received",
                    Group = "workers",
                    IntentType = IntentType.Queue,
                    Content = "{\"received\":\"data\"}",
                    Added = new DateTimeOffset(2026, 03, 24, 11, 00, 00, TimeSpan.Zero),
                    Retries = 1,
                    StatusName = StatusName.Failed,
                },
            ],
            index: 0,
            size: 10,
            totalItems: 1
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
            "/api/received/Failed?currentPage=1&perPage=10&group=workers&intentType=Queue",
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
        payload["index"].GetInt32().Should().Be(0);
        payload["size"].GetInt32().Should().Be(10);
        payload["totalItems"].GetInt32().Should().Be(1);
        payload["totals"].GetInt32().Should().Be(1);

        var item = payload["items"].EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("storageId").GetString().Should().Be("11111111-1111-1111-1111-111111111456");
        item.GetProperty("messageId").GetString().Should().Be("logical-rec-456");
        item.GetProperty("group").GetString().Should().Be("workers");
        item.GetProperty("intentType").GetInt32().Should().Be((int)IntentType.Queue);

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Subscribe
                    && query.StatusName == StatusName.Failed
                    && query.Group == "workers"
                    && query.IntentType == IntentType.Queue
                    && query.CurrentPage == 0
                    && query.PageSize == 10
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_bind_null_intent_filter_when_received_list_intent_type_is_omitted()
    {
        // given
        var result = new IndexPage<MessageView>([], index: 0, size: 10, totalItems: 0);

        _monitoringApi
            .GetMessagesAsync(Arg.Any<MessageQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when — intentType omitted from query string
        var response = await client.GetAsync("/api/received/Failed?currentPage=1&perPage=10&group=workers", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Subscribe
                    && query.StatusName == StatusName.Failed
                    && query.Group == "workers"
                    && query.IntentType == null
                    && query.CurrentPage == 0
                    && query.PageSize == 10
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_422_for_empty_array_when_received_requeue()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/received/reexecute", Array.Empty<long>(), AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task should_return_422_for_null_body_when_received_delete()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        using var stringContent = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/received/delete", stringContent, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task should_return_204_on_success_when_received_delete()
    {
        // given
        var messageId = new Guid(0x11111111, 0x1111, 0x1111, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x17, 0x89);
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage
            .DeleteReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(1));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/received/delete", new[] { messageId }, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
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
        appBuilder.Services.AddMemoryCache();
        appBuilder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        appBuilder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        appBuilder.Services.AddSingleton<GatewayProxyAgent>();

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
