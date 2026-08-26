// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Jobs;
using Headless.Jobs.Authorization;
using Headless.Jobs.Endpoints;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
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
        names.Should().Contain("GetLiveNodes");
        names.Should().Contain("GetJobRequest");
    }

    private static WebApplication _CreateApp(DashboardOptionsBuilder config)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddSignalR();
        builder.Services.AddCors();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<JobsDashboardAuthorizer>();
        builder.Services.AddSingleton(Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(new JobsExecutionContext());
        builder.Services.AddSingleton(new SchedulerOptionsBuilder());
        builder.Services.AddSingleton(Substitute.For<IJobsDashboardRepository<TimeJobEntity, CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<ITimeJobManager<TimeJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<ICronJobManager<CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<IJobScheduler>());
        builder.Services.AddSingleton(Substitute.For<IJobsHostScheduler>());
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("DashboardAdmin", policy => policy.RequireAssertion(_ => true))
        );

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
