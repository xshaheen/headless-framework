// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Jobs;
using Headless.Jobs.Endpoints;
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
    [Fact]
    public async Task should_apply_the_configured_host_policy_only_to_dashboard_api_endpoints()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication("DashboardAdmin"));

        _GetEndpoint(app, "GetAuthInfo").Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        _GetEndpoint(app, "ValidateAuth").Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        _GetEndpoint(app, "GetTimeJobsPaginated")
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == "DashboardAdmin");
        _GetHubEndpoint(app).Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_use_default_authorization_when_host_auth_has_no_named_policy()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());

        var authorization = _GetEndpoint(app, "GetOptions").Metadata.GetOrderedMetadata<IAuthorizeData>();

        authorization.Should().ContainSingle();
        authorization[0].Policy.Should().BeNull();
    }

    [Fact]
    public async Task should_not_attach_host_authorization_metadata_when_auth_is_handled_by_dashboard_middleware()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithApiKey("secret"));

        _GetEndpoint(app, "GetOptions").Metadata.GetOrderedMetadata<IAuthorizeData>().Should().BeEmpty();
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
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) is true)
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

    [Theory]
    [InlineData("RequeueJob", "/api/job/requeue")]
    [InlineData("RequeueCronJobOccurrence", "/api/cron-job-occurrence/requeue")]
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
        var body = System.Text.Encoding.UTF8.GetBytes("""{"function":"sandbox.quiet"}""");
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Post;
        context.Request.QueryString = new QueryString(query);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

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
        var body = System.Text.Encoding.UTF8.GetBytes(
            """
            {"function":"root","executionTime":"2026-03-01T09:00:00","children":[
              {"function":"child","executionTime":"2026-03-01T10:00:00","runCondition":0,"children":[
                {"function":"grandchild","executionTime":"2026-03-01T11:00:00Z","runCondition":0}]}]}
            """
        );
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Post;
        context.Request.QueryString = new QueryString("?timeZoneId=Asia/Tokyo");
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await _GetEndpoint(app, "CreateChainJobs").RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        added!.ExecutionTime.Should().Be(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var child = added.Children.Single();
        child.ExecutionTime.Should().Be(new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc));
        child.Children.Single().ExecutionTime.Should().Be(new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc));
    }

    private static WebApplication _CreateApp(DashboardOptionsBuilder config)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddSignalR();
        builder.Services.AddCors();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
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

        var app = builder.Build();
        app.MapDashboardEndpoints<TimeJobEntity, CronJobEntity>(config);
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
                && string.Equals(routeEndpoint.RoutePattern.RawText, "/job-notification-hub", StringComparison.Ordinal)
            );
    }

    private static IReadOnlyList<Endpoint> _Endpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
    }
}
