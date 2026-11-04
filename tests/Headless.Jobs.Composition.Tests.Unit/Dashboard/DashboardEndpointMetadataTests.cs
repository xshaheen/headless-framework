// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Dashboard;

public sealed class DashboardEndpointMetadataTests : TestBase
{
    private const string _BasePath = "/jobs/dashboard";

    [Fact]
    public async Task should_apply_the_configured_host_policy_to_dashboard_api_and_hub_endpoints()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication("DashboardAdmin"));

        _GetEndpoint(app, "GetAuthInfo").Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        _GetEndpoint(app, "ValidateAuth").Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        _GetEndpoint(app, "GetTimeJobsPaginated")
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == "DashboardAdmin");
        _GetHubEndpoint(app).Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        _GetHubEndpoint(app)
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == "DashboardAdmin");
    }

    [Theory]
    [InlineData(AuthMode.Basic)]
    [InlineData(AuthMode.ApiKey)]
    public async Task should_apply_the_dashboard_policy_to_the_api_and_hub_under_a_dashboard_credential(AuthMode mode)
    {
        // The hub carries the API's rule, so the host's UseAuthorization refuses a client before SignalR adds it to
        // Clients.All, where it would hear a broadcast before the hub's OnConnectedAsync ran.
        var config =
            mode == AuthMode.Basic
                ? new DashboardOptionsBuilder().WithBasicAuth("ops", "secret")
                : new DashboardOptionsBuilder().WithApiKey("secret");
        await using var app = _CreateApp(config);

        foreach (var endpoint in new[] { _GetEndpoint(app, "GetOptions"), _GetHubEndpoint(app) })
        {
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
            endpoint
                .Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Should()
                .ContainSingle(data => data.Policy == DashboardOptionsBuilder.AuthenticationName);
        }
    }

    [Fact]
    public async Task should_leave_the_api_and_hub_anonymous_when_no_auth_is_selected()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());

        _GetEndpoint(app, "GetOptions").Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        _GetHubEndpoint(app).Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("Jobs_Spa")]
    [InlineData("GetAuthInfo")]
    [InlineData("ValidateAuth")]
    public async Task should_keep_the_shell_and_auth_endpoints_anonymous_under_a_dashboard_credential(
        string endpointName
    )
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithBasicAuth("ops", "secret"));

        _GetEndpoint(app, endpointName).Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_default_authorization_when_host_auth_has_no_named_policy()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());

        var authorization = _GetEndpoint(app, "GetOptions").Metadata.GetOrderedMetadata<IAuthorizeData>();

        authorization.Should().ContainSingle();
        authorization[0].Policy.Should().BeNull();
    }

    [Theory]
    [InlineData("CreateChainJobs")]
    [InlineData("UpdateTimeJob")]
    [InlineData("DeleteTimeJobsBatch")]
    [InlineData("AddCronJob")]
    [InlineData("UpdateCronJob")]
    public async Task should_cap_request_bodies_on_mutating_payload_endpoints(string endpointName)
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());

        var requestSizeLimit = _GetEndpoint(app, endpointName).Metadata.GetMetadata<RequestSizeLimitAttribute>();

        requestSizeLimit.Should().NotBeNull();
        ((IRequestSizeLimitMetadata)requestSizeLimit!)
            .MaxRequestBodySize.Should()
            .Be(DashboardOptionsBuilder.MaxRequestBodyBytes);
    }

    [Fact]
    public async Task should_expose_unique_endpoint_names_for_every_dashboard_route()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());
        var names = _Endpoints(app)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText?.StartsWith(_BasePath + "/api", StringComparison.Ordinal) is true
            )
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToArray();

        names.Should().NotContainNulls();
        names.OfType<string>().Should().OnlyHaveUniqueItems();
        names.Should().Contain("CancelJob");
        names.Should().Contain("RequeueJob");
        names.Should().Contain("RequeueCronJobOccurrence");
        names.Should().Contain("GetLiveNodes");
        names.Should().Contain("GetJobRequest");
    }

    [Fact]
    public async Task should_exclude_every_dashboard_api_endpoint_from_application_api_descriptions()
    {
        // The dashboard SPA is the only client of these endpoints, so a host's API document never lists them.
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());

        var api = _Endpoints(app)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText?.StartsWith(_BasePath + "/api", StringComparison.Ordinal) is true
            )
            .ToArray();

        api.Should()
            .Contain(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName == "GetAuthInfo");
        api.Should()
            .OnlyContain(endpoint =>
                endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>() != null
                && endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()!.ExcludeFromDescription
            );
    }

    [Theory]
    [InlineData("RequeueJob", "/jobs/dashboard/api/job/requeue")]
    [InlineData("RequeueCronJobOccurrence", "/jobs/dashboard/api/cron-job-occurrence/requeue")]
    public async Task should_map_requeue_as_a_post_endpoint(string endpointName, string route)
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());

        var endpoint = (RouteEndpoint)_GetEndpoint(app, endpointName);

        endpoint.RoutePattern.RawText.Should().Be(route);
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal("POST");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?timeZoneId=UTC")]
    public async Task should_create_a_chain_with_or_without_a_time_zone_query_parameter(string query)
    {
        // The chain wizard posts without timeZoneId; binding it as required rejected every chain with 400 before
        // the handler, which already treats a missing zone as "the time is UTC", could run.
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());
        var manager = app.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        manager
            .AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<TimeJobEntity>()));
        var context = _Request(app, HttpMethods.Post, query, """{"function":"sandbox.quiet"}""");

        await _GetEndpoint(app, "CreateChainJobs").RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        await manager.Received(1).AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_every_zone_less_chain_time_in_the_requested_zone_and_keep_explicit_instants()
    {
        // The same case-insensitive camelCase options the dashboard registration installs.
        await using var app = _CreateApp(
            new DashboardOptionsBuilder()
                .WithNoAuth()
                .ConfigureDashboardJsonOptions(json =>
                {
                    json.PropertyNameCaseInsensitive = true;
                    json.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                })
        );
        var manager = app.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        TimeJobEntity? added = null;
        manager
            .AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(added = call.Arg<TimeJobEntity>()));
        // Asia/Tokyo has no daylight saving time, so +09:00 holds for any date.
        const string body = """
            {"function":"root","executionTime":"2026-03-01T09:00:00","children":[
              {"function":"child","executionTime":"2026-03-01T10:00:00","runCondition":0,"children":[
                {"function":"grandchild","executionTime":"2026-03-01T11:00:00Z","runCondition":0}]}]}
            """;
        var context = _Request(app, HttpMethods.Post, "?timeZoneId=Asia/Tokyo", body);

        await _GetEndpoint(app, "CreateChainJobs").RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        added!.ExecutionTime.Should().Be(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var child = added.Children.Single();
        child.ExecutionTime.Should().Be(new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc));
        child.Children.Single().ExecutionTime.Should().Be(new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("StopJobHost", "")]
    [InlineData("StartJobHost", "")]
    [InlineData("RestartJobHost", "")]
    [InlineData("CancelJob", "?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("RequeueJob", "?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("RequeueCronJobOccurrence", "?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("_RunCronJobOnDemand", "?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("DeleteTimeJob", "?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    public async Task should_refuse_a_state_changing_request_without_the_dashboard_request_header(
        string endpointName,
        string query
    )
    {
        // A cross-site <form> posts with the host's cookie but cannot set a request header.
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());
        var endpoint = (RouteEndpoint)_GetEndpoint(app, endpointName);
        var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single();
        var context = _Request(app, method, query, body: null, forgeryHeader: false);

        await endpoint.RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        app.Services.GetRequiredService<IJobsHostScheduler>().ReceivedCalls().Should().BeEmpty();
        app.Services.GetRequiredService<IJobScheduler>().ReceivedCalls().Should().BeEmpty();
        app.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>().ReceivedCalls().Should().BeEmpty();
        app.Services.GetRequiredService<IJobsDashboardRepository<TimeJobEntity, CronJobEntity>>()
            .ReceivedCalls()
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task should_run_a_state_changing_request_that_carries_the_dashboard_request_header()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());
        var context = _Request(app, HttpMethods.Post, query: "", body: null);

        await _GetEndpoint(app, "StopJobHost").RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        await app.Services.GetRequiredService<IJobsHostScheduler>().Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_serve_a_read_without_the_dashboard_request_header()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());
        var context = _Request(app, HttpMethods.Get, query: "", body: null, forgeryHeader: false);

        await _GetEndpoint(app, "GetOptions").RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_refuse_a_json_body_sent_as_a_form_content_type()
    {
        // A cross-site <form enctype="text/plain"> can shape its body into valid JSON.
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());
        var manager = app.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var context = _Request(
            app,
            HttpMethods.Post,
            query: "",
            """{"function":"sandbox.quiet"}""",
            contentType: "text/plain"
        );

        await _GetEndpoint(app, "CreateChainJobs").RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        manager.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("CreateChainJobs")]
    [InlineData("UpdateTimeJob")]
    public async Task should_reject_an_unknown_time_zone_with_bad_request(string endpointName)
    {
        // The same case-insensitive camelCase options the dashboard registration installs, so executionTime binds.
        await using var app = _CreateApp(
            new DashboardOptionsBuilder()
                .WithNoAuth()
                .ConfigureDashboardJsonOptions(json =>
                {
                    json.PropertyNameCaseInsensitive = true;
                    json.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                })
        );
        var manager = app.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        var endpoint = (RouteEndpoint)_GetEndpoint(app, endpointName);
        var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single();
        var context = _Request(
            app,
            method,
            "?id=6f9619ff-8b86-d011-b42d-00cf4fc964ff&timeZoneId=Not/AZone",
            """{"function":"sandbox.quiet","executionTime":"2026-03-01T09:00:00"}"""
        );

        await endpoint.RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        manager.ReceivedCalls().Should().BeEmpty();
    }

    private static DefaultHttpContext _Request(
        WebApplication app,
        string method,
        string query,
        string? body,
        string contentType = "application/json",
        bool forgeryHeader = true
    )
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = method;
        context.Request.QueryString = new QueryString(query);
        if (forgeryHeader)
        {
            context.Request.Headers.XRequestedWith = "XMLHttpRequest";
        }

        if (body is not null)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            context.Request.ContentType = contentType;
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static WebApplication _CreateApp(DashboardOptionsBuilder config)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddSignalR();
        builder.Services.AddCors();
        builder.Services.AddSingleton(config);
        builder.Services.AddKeyedSingleton(DashboardOptionsBuilder.AuthenticationName, Substitute.For<IAuthService>());
        // The endpoints read the mode from this named AuthConfig when the host builds them, as AddDashboard registers it.
        builder.Services.AddDashboardAuthentication(
            DashboardOptionsBuilder.AuthenticationName,
            auth =>
            {
                auth.Mode = config.Auth.Mode;
                auth.BasicCredentials = config.Auth.BasicCredentials;
                auth.ApiKey = config.Auth.ApiKey;
                auth.Host = config.Auth.Host;
            }
        );
        builder.Services.AddSingleton(new JobsExecutionContext());
        builder.Services.AddSingleton(JobFunctionRegistryBuilder.Build([], [], []));
        builder.Services.AddSingleton(new SchedulerOptionsBuilder());
        builder.Services.AddSingleton(Substitute.For<IJobsDashboardRepository<TimeJobEntity, CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<ITimeJobManager<TimeJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<ICronJobManager<CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<IJobScheduler>());
        builder.Services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("DashboardAdmin", policy => policy.RequireAssertion(_ => true));

        // AddDashboard sets this delegate; these tests register substitutes instead of the Jobs services.
        config.MapEndpoints = group => group.MapDashboard<TimeJobEntity, CronJobEntity>(config);

        var app = builder.Build();
        app.MapHeadlessJobsDashboard();
        return app;
    }

    private static Endpoint _GetEndpoint(WebApplication app, string endpointName)
    {
        return _Endpoints(app)
            .Single(endpoint =>
                string.Equals(
                    endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    endpointName,
                    StringComparison.Ordinal
                )
            );
    }

    private static Endpoint _GetHubEndpoint(WebApplication app)
    {
        return _Endpoints(app)
            .Single(endpoint =>
                endpoint is RouteEndpoint routeEndpoint
                && string.Equals(
                    routeEndpoint.RoutePattern.RawText,
                    _BasePath + "/job-notification-hub",
                    StringComparison.Ordinal
                )
            );
    }

    private static IReadOnlyList<Endpoint> _Endpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
    }
}
