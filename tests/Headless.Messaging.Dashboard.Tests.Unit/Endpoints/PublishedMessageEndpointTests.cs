// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Microsoft.Extensions.Hosting;

namespace Tests.Endpoints;

public sealed class PublishedMessageEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task PublishedMessageDetails_should_return_message_content()
    {
        // given
        const long messageId = 123;
        var message = new MediumMessage
        {
            StorageId = messageId,
            Content = "{\"key\":\"value\"}",
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
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/published/message/{messageId}");

        // then
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        payload.Should().ContainKey("storageId");
        payload.Should().ContainKey("messageId");
    }

    [Fact]
    public async Task PublishedMessageDetails_should_return_404_for_missing_message()
    {
        // given
        const long messageId = 999;
        _monitoringApi
            .GetPublishedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<MediumMessage?>(null));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/published/message/{messageId}");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishedList_should_preserve_pagination_metadata_and_map_identity_fields()
    {
        // given
        var result = new IndexPage<MessageView>(
            [
                new MessageView
                {
                    StorageId = 123,
                    MessageId = "logical-pub-123",
                    Version = "v1",
                    Name = "orders.created",
                    Content = "{\"key\":\"value\"}",
                    Added = new DateTime(2026, 03, 24, 10, 00, 00, DateTimeKind.Utc),
                    Retries = 2,
                    StatusName = "Succeeded",
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
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/published/Succeeded?currentPage=2&perPage=20");

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        payload
            .Should()
            .ContainKeys("items", "index", "size", "totalItems", "totalPages", "hasPrevious", "hasNext", "totals");
        payload["index"].GetInt32().Should().Be(1);
        payload["size"].GetInt32().Should().Be(20);
        payload["totalItems"].GetInt32().Should().Be(35);
        payload["totals"].GetInt32().Should().Be(35);

        var item = payload["items"].EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("storageId").GetString().Should().Be("123");
        item.GetProperty("messageId").GetString().Should().Be("logical-pub-123");

        await _monitoringApi
            .Received(1)
            .GetMessagesAsync(
                Arg.Is<MessageQuery>(query =>
                    query.MessageType == MessageType.Publish
                    && query.StatusName == "Succeeded"
                    && query.CurrentPage == 1
                    && query.PageSize == 20
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PublishedRequeue_should_return_422_for_empty_array()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/published/requeue", Array.Empty<long>());

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PublishedDelete_should_return_422_for_null_body()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsync(
            "/api/published/delete",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PublishedDelete_should_return_204_on_success()
    {
        // given
        const long messageId = 123;
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);
        _dataStorage
            .DeletePublishedMessageAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(1));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync("/api/published/delete", new[] { messageId });

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
