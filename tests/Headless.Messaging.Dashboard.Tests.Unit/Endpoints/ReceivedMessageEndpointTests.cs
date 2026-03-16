// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.GatewayProxy.Requester;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            DbId = messageId.ToString(CultureInfo.InvariantCulture),
            Content = "{\"received\":\"data\"}",
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal) { { "test", "header" } },
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
        appBuilder.Services.AddSingleton(Substitute.For<IHttpRequester>());
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
