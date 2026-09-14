// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

public sealed class ScheduledDeliveryEndpointTests : TestBase
{
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();
    private readonly IScheduledDeliveryOperationsApi _operations = Substitute.For<IScheduledDeliveryOperationsApi>();

    public ScheduledDeliveryEndpointTests()
    {
        _dataStorage.GetScheduledDeliveryOperationsApi().Returns(_operations);
    }

    [Fact]
    public async Task should_clamp_page_size_and_project_full_precision_due_instant_when_scheduled_list()
    {
        // given
        var storageId = Guid.Parse("22222222-2222-2222-2222-222222222001");
        var dueAt = new DateTimeOffset(2026, 09, 14, 10, 30, 0, TimeSpan.Zero).AddTicks(1234567);
        var result = new IndexPage<ScheduledDeliveryView>(
            [
                new ScheduledDeliveryView(
                    storageId,
                    "msg-2001",
                    "orders.created",
                    MessageLane.Bus,
                    dueAt,
                    "Pending",
                    true,
                    "node-1",
                    dueAt.AddMinutes(2),
                    0
                ),
            ],
            index: 0,
            size: 200,
            totalItems: 1
        );
        _operations
            .QueryAsync(
                Arg.Any<ScheduledDeliveryQuery>(),
                Arg.Any<OperatorAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(result));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/scheduled?perPage=500&currentPage=1", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var item = document.RootElement.GetProperty("items")[0];
        item.GetProperty("storageId").GetString().Should().Be(storageId.ToString());
        item.GetProperty("isLeased").GetBoolean().Should().BeTrue();
        item.GetProperty("expectedDueAt").GetString().Should().Be(dueAt.ToString("o"));
        await _operations
            .Received(1)
            .QueryAsync(
                Arg.Is<ScheduledDeliveryQuery>(query => query.PageSize == 200 && query.CurrentPage == 0),
                Arg.Any<OperatorAuthorizationContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("/api/scheduled/revoke", InboxOperationOutcome.Applied, HttpStatusCode.OK)]
    [InlineData("/api/scheduled/revoke", InboxOperationOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData("/api/scheduled/revoke", InboxOperationOutcome.StateConflict, HttpStatusCode.Conflict)]
    [InlineData("/api/scheduled/revoke", InboxOperationOutcome.Active, HttpStatusCode.Conflict)]
    [InlineData("/api/scheduled/dispatch-now", InboxOperationOutcome.Applied, HttpStatusCode.OK)]
    [InlineData("/api/scheduled/dispatch-now", InboxOperationOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData("/api/scheduled/dispatch-now", InboxOperationOutcome.Active, HttpStatusCode.Conflict)]
    public async Task should_route_scheduled_mutation_and_map_outcome_to_status_code(
        string path,
        InboxOperationOutcome outcome,
        HttpStatusCode expectedStatusCode
    )
    {
        var operationId = Guid.NewGuid();
        var storageId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var result = new ScheduledDeliveryOperationResult(
            operationId,
            path.EndsWith("revoke", StringComparison.Ordinal)
                ? MessagingOperationType.Revoke
                : MessagingOperationType.DispatchNow,
            outcome,
            storageId,
            dueAt,
            "orders.created",
            "msg-1",
            MessageLane.Bus,
            "dashboard-operator",
            "Dashboard action",
            DateTimeOffset.UtcNow
        );
        _operations
            .RevokeAsync(Arg.Any<ScheduledDeliveryOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        _operations
            .DispatchNowAsync(Arg.Any<ScheduledDeliveryOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.PostAsJsonAsync(
            path,
            new
            {
                operationId,
                storageId,
                expectedDueAt = dueAt.ToString("o"),
                reason = "Dashboard action",
            },
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(expectedStatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("outcome").GetString().Should().Be(outcome.ToString());
        document.RootElement.GetProperty("actor").GetString().Should().Be("dashboard-operator");
    }

    [Fact]
    public async Task should_return_200_with_replay_flag_when_scheduled_revoke_replays()
    {
        var operationId = Guid.NewGuid();
        var storageId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var result = new ScheduledDeliveryOperationResult(
            operationId,
            MessagingOperationType.Revoke,
            InboxOperationOutcome.Applied,
            storageId,
            dueAt,
            "orders.created",
            "msg-1",
            MessageLane.Bus,
            "dashboard-operator",
            "Dashboard revoke",
            DateTimeOffset.UtcNow,
            IsReplay: true
        );
        _operations
            .RevokeAsync(Arg.Any<ScheduledDeliveryOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/scheduled/revoke",
            new
            {
                operationId,
                storageId,
                expectedDueAt = dueAt.ToString("o"),
                reason = "Dashboard revoke",
            },
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("isReplay").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/scheduled")]
    [InlineData("/api/scheduled/revoke")]
    [InlineData("/api/scheduled/dispatch-now")]
    public async Task should_return_401_for_unauthenticated_scheduled_request(string path)
    {
        await using var app = _CreateTestApp(_dataStorage, authenticate: false);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(path == "/api/scheduled" ? HttpMethod.Get : HttpMethod.Post, path);
        if (request.Method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _dataStorage.DidNotReceive().GetScheduledDeliveryOperationsApi();
    }

    [Fact]
    public async Task should_return_403_with_remedy_for_host_placeholder_actor_when_scheduled_revoke()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("host_role", "operator")], "test", ClaimTypes.Name, "host_role")
        );
        await using var app = _CreateTestApp(_dataStorage, authenticate: false, principal: principal);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/scheduled/revoke",
            new
            {
                operationId = Guid.NewGuid(),
                storageId = Guid.NewGuid(),
                expectedDueAt = DateTimeOffset.UtcNow.ToString("o"),
                reason = "Dashboard revoke",
            },
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("code").GetString().Should().Be("g:operator_actor_required");
        _dataStorage.DidNotReceive().GetScheduledDeliveryOperationsApi();
    }

    [Fact]
    public async Task should_authorize_scheduled_mutation_with_builtin_api_key_identity()
    {
        var operationId = Guid.NewGuid();
        var storageId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var result = new ScheduledDeliveryOperationResult(
            operationId,
            MessagingOperationType.Revoke,
            InboxOperationOutcome.Applied,
            storageId,
            dueAt,
            "orders.created",
            "msg-1",
            MessageLane.Bus,
            "api-user",
            "Dashboard revoke",
            DateTimeOffset.UtcNow
        );
        _operations
            .RevokeAsync(Arg.Any<ScheduledDeliveryOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        await using var app = _CreateTestApp(
            _dataStorage,
            authenticate: false,
            config: new MessagingDashboardOptionsBuilder().WithApiKey("secret"),
            useAuthenticationMiddleware: true
        );
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret");
        var response = await client.PostAsJsonAsync(
            "/api/scheduled/revoke",
            new
            {
                operationId,
                storageId,
                expectedDueAt = dueAt.ToString("o"),
                reason = "Dashboard revoke",
            },
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _operations
            .Received(1)
            .RevokeAsync(
                Arg.Is<ScheduledDeliveryOperationRequest>(request => request.Actor == "api-user"),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("/api/scheduled/revoke")]
    [InlineData("/api/scheduled/dispatch-now")]
    public async Task should_return_415_for_non_json_scheduled_mutation(string path)
    {
        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var content = new StringContent("{}", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(path, content, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _dataStorage.DidNotReceive().GetScheduledDeliveryOperationsApi();
    }

    [Theory]
    [InlineData("/api/scheduled/revoke")]
    [InlineData("/api/scheduled/dispatch-now")]
    public async Task should_return_422_for_malformed_json_scheduled_mutation(string path)
    {
        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(path, content, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _dataStorage.DidNotReceive().GetScheduledDeliveryOperationsApi();
    }

    [Fact]
    public async Task should_round_trip_scheduled_json_independently_of_host_options()
    {
        var operationId = Guid.NewGuid();
        var storageId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var result = new ScheduledDeliveryOperationResult(
            operationId,
            MessagingOperationType.Revoke,
            InboxOperationOutcome.Applied,
            storageId,
            dueAt,
            "orders.created",
            "msg-1",
            MessageLane.Bus,
            "dashboard-operator",
            "Dashboard revoke",
            DateTimeOffset.UtcNow
        );
        _operations
            .RevokeAsync(Arg.Any<ScheduledDeliveryOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(result));
        await using var app = _CreateTestApp(_dataStorage, customizeHostJson: true);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/scheduled/revoke",
            new
            {
                operationId,
                storageId,
                expectedDueAt = dueAt.ToString("o"),
                reason = "Dashboard revoke",
            },
            AbortToken
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document.RootElement.GetProperty("outcome").GetString().Should().Be(nameof(InboxOperationOutcome.Applied));
        document.RootElement.GetProperty("lane").GetString().Should().Be(nameof(MessageLane.Bus));
    }

    private static WebApplication _CreateTestApp(
        IDataStorage dataStorage,
        bool authenticate = true,
        MessagingDashboardOptionsBuilder? config = null,
        bool useAuthenticationMiddleware = false,
        bool customizeHostJson = false,
        ClaimsPrincipal? principal = null
    )
    {
        config ??= new MessagingDashboardOptionsBuilder().WithNoAuth();

        var appBuilder = WebApplication.CreateSlimBuilder();
        appBuilder.WebHost.UseTestServer();
        if (customizeHostJson)
        {
            appBuilder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });
        }

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

        appBuilder.Services.AddRouting();
        appBuilder.Services.AddAuthorization();
        if (config.Auth.Mode == AuthMode.Host)
        {
            appBuilder
                .Services.AddAuthentication("test")
                .AddCookie(
                    "test",
                    options =>
                    {
                        options.Events.OnRedirectToAccessDenied = context =>
                        {
                            context.Response.StatusCode = 403;
                            return Task.CompletedTask;
                        };
                    }
                );
        }
        appBuilder.Services.AddCors(o => o.AddPolicy("HeadlessMessagingDashboardCORS", p => p.AllowAnyOrigin()));

        var app = appBuilder.Build();
        app.UseRouting();
        if (principal is not null)
        {
            app.Use(
                (context, next) =>
                {
                    context.User = principal;
                    return next(context);
                }
            );
        }
        if (useAuthenticationMiddleware)
        {
            app.UseMiddleware<AuthMiddleware>();
        }
        else if (authenticate && principal is null)
        {
            app.Use(
                (context, next) =>
                {
                    context.User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim(ClaimTypes.Name, "dashboard-operator")],
                            authenticationType: "test"
                        )
                    );
                    return next(context);
                }
            );
        }
        app.UseCors("HeadlessMessagingDashboardCORS");
        app.UseAuthorization();
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }
}
