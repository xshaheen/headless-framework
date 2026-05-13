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
    public async Task ReceivedMessageDetails_should_return_message_content()
    {
        // given
        const long messageId = 456;
        var message = new MediumMessage
        {
            StorageId = messageId,
            Content = "{\"received\":\"data\"}",
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
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/received/message/{messageId}");

        // then
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        payload.Should().ContainKey("storageId");
        payload.Should().ContainKey("messageId");
    }

    [Fact]
    public async Task ReceivedMessageDetails_should_return_404_for_missing_message()
    {
        // given
        const long messageId = 888;
        _monitoringApi
            .GetReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(null));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/received/message/{messageId}");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReceivedList_should_preserve_pagination_metadata_and_map_identity_fields()
    {
        // given
        var result = new IndexPage<MessageView>(
            [
                new MessageView
                {
                    StorageId = 456,
                    MessageId = "logical-rec-456",
                    Version = "v1",
                    Name = "orders.received",
                    Group = "workers",
                    Content = "{\"received\":\"data\"}",
                    Added = new DateTime(2026, 03, 24, 11, 00, 00, DateTimeKind.Utc),
                    Retries = 1,
                    StatusName = "Failed",
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
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/received/Failed?currentPage=1&perPage=10&group=workers");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        payload
            .Should()
            .ContainKeys("items", "index", "size", "totalItems", "totalPages", "hasPrevious", "hasNext", "totals");
        payload["index"].GetInt32().Should().Be(0);
        payload["size"].GetInt32().Should().Be(10);
        payload["totalItems"].GetInt32().Should().Be(1);
        payload["totals"].GetInt32().Should().Be(1);

        var item = payload["items"].EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("storageId").GetString().Should().Be("456");
        item.GetProperty("messageId").GetString().Should().Be("logical-rec-456");
        item.GetProperty("group").GetString().Should().Be("workers");

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Subscribe
                    && query.StatusName == "Failed"
                    && query.Group == "workers"
                    && query.CurrentPage == 0
                    && query.PageSize == 10
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReceivedRequeue_should_return_422_for_empty_array()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/received/reexecute", Array.Empty<long>());

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ReceivedDelete_should_return_422_for_null_body()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsync(
            "/api/received/delete",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ReceivedDelete_should_return_204_on_success()
    {
        // given
        const long messageId = 789;
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage
            .DeleteReceivedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(1));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/received/delete", new[] { messageId });

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
        appBuilder.Services.AddCors(o => o.AddPolicy("Messaging_Dashboard_CORS", p => p.AllowAnyOrigin()));

        var app = appBuilder.Build();
        app.UseRouting();
        app.UseCors("Messaging_Dashboard_CORS");
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }
}
