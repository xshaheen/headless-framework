// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Constants;
using Headless.Dashboard.Authentication;
using Headless.Jobs;
using Headless.Jobs.Authorization;
using Headless.Jobs.Endpoints;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Dashboard;

public sealed class DashboardAuthorizationTests : TestBase
{
    // The auditable route table: anonymous / read / tenant-row mutation / admin. Every mapped route must appear here
    // with exactly one class, so a newly added route fails this test until it is classified deliberately.
    private static readonly Dictionary<string, DashboardAccess> _ExpectedAccess = new(StringComparer.Ordinal)
    {
        ["GET /api/auth/info"] = DashboardAccess.Anonymous,
        ["POST /api/auth/validate"] = DashboardAccess.Anonymous,
        ["GET /api/options"] = DashboardAccess.Read,
        ["GET /api/time-jobs/paginated"] = DashboardAccess.Read,
        ["GET /api/time-jobs/graph-data-range"] = DashboardAccess.Read,
        ["GET /api/time-jobs/graph-data"] = DashboardAccess.Read,
        ["POST /api/time-job/add"] = DashboardAccess.Admin,
        ["PUT /api/time-job/update"] = DashboardAccess.TenantRowMutation,
        ["DELETE /api/time-job/delete"] = DashboardAccess.TenantRowMutation,
        ["DELETE /api/time-job/delete-batch"] = DashboardAccess.Admin,
        ["GET /api/cron-jobs/paginated"] = DashboardAccess.Read,
        ["GET /api/cron-jobs/graph-data-range"] = DashboardAccess.Read,
        ["GET /api/cron-jobs/graph-data-range-id"] = DashboardAccess.Read,
        ["GET /api/cron-jobs/graph-data"] = DashboardAccess.Read,
        ["GET /api/cron-job-occurrences/{cronJobId}/paginated"] = DashboardAccess.Read,
        ["GET /api/cron-job-occurrences/{cronJobId}/graph-data"] = DashboardAccess.Read,
        ["POST /api/cron-job/add"] = DashboardAccess.Admin,
        ["PUT /api/cron-job/update"] = DashboardAccess.Admin,
        ["POST /api/cron-job/run"] = DashboardAccess.Admin,
        ["DELETE /api/cron-job/delete"] = DashboardAccess.Admin,
        ["DELETE /api/cron-job-occurrence/delete"] = DashboardAccess.Admin,
        ["POST /api/job/cancel"] = DashboardAccess.TenantRowMutation,
        ["GET /api/job-request/id"] = DashboardAccess.Read,
        ["GET /api/job-functions"] = DashboardAccess.Read,
        ["GET /api/job-host/next-job"] = DashboardAccess.Read,
        ["POST /api/job-host/stop"] = DashboardAccess.Admin,
        ["POST /api/job-host/start"] = DashboardAccess.Admin,
        ["POST /api/job-host/restart"] = DashboardAccess.Admin,
        ["GET /api/job-host/status"] = DashboardAccess.Read,
        ["GET /api/job/statuses/get-last-week"] = DashboardAccess.Read,
        ["GET /api/job/statuses/get"] = DashboardAccess.Read,
        ["GET /api/job/machine/jobs"] = DashboardAccess.Read,
        ["GET /api/nodes"] = DashboardAccess.Read,
        // The hub and its SignalR negotiate leg share one convention builder; both are enforced on connect.
        ["HUB /job-notification-hub"] = DashboardAccess.Read,
        ["HUB /job-notification-hub/negotiate"] = DashboardAccess.Read,
    };

    [Fact]
    public async Task should_classify_every_mapped_route_in_exactly_one_access_class()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());

        var actual = new Dictionary<string, DashboardAccess>(StringComparer.Ordinal);
        foreach (var endpoint in _Endpoints(app).OfType<RouteEndpoint>())
        {
            var access = endpoint.Metadata.GetOrderedMetadata<DashboardAccessMetadata>();
            access.Should().ContainSingle($"route {endpoint.RoutePattern.RawText} must carry exactly one access class");
            actual[_RouteKey(endpoint)] = access[0].Access;
        }

        actual.Should().BeEquivalentTo(_ExpectedAccess);
    }

    [Fact]
    public void should_expose_permission_constants_without_duplicated_literals()
    {
        JobsDashboardPermissions.Read.Should().Be("headless.jobs.read");
        JobsDashboardPermissions.Admin.Should().Be("headless.jobs.admin");
    }

    [Fact]
    public async Task should_return_401_for_an_unauthenticated_host_caller()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());

        var status = await _InvokeAsync(app, "GetOptions", user: null);

        status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData("GetOptions", JobsDashboardPermissions.Read, StatusCodes.Status200OK)]
    [InlineData("GetOptions", JobsDashboardPermissions.Admin, StatusCodes.Status200OK)]
    [InlineData("GetOptions", null, StatusCodes.Status403Forbidden)]
    [InlineData("StopJobHost", JobsDashboardPermissions.Read, StatusCodes.Status403Forbidden)]
    [InlineData("StopJobHost", JobsDashboardPermissions.Admin, StatusCodes.Status200OK)]
    [InlineData("AddCronJob", JobsDashboardPermissions.Read, StatusCodes.Status403Forbidden)]
    [InlineData("_RunCronJobOnDemand", JobsDashboardPermissions.Read, StatusCodes.Status403Forbidden)]
    [InlineData("DeleteCronJob", JobsDashboardPermissions.Read, StatusCodes.Status403Forbidden)]
    [InlineData("DeleteTimeJobsBatch", JobsDashboardPermissions.Read, StatusCodes.Status403Forbidden)]
    [InlineData("CreateChainJobs", JobsDashboardPermissions.Read, StatusCodes.Status403Forbidden)]
    public async Task should_separate_read_from_admin_under_host_authentication(
        string endpointName,
        string? permission,
        int expectedStatus
    )
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication());

        var status = await _InvokeAsync(app, endpointName, _HostUser(permission));

        status.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task should_not_mutate_a_system_cron_definition_for_a_non_admin_tenant_caller()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant: "t-a");
        var cronManager = app.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
        var repository = app.Services.GetRequiredService<IJobsDashboardRepository<TimeJobEntity, CronJobEntity>>();

        (await _InvokeAsync(app, "DeleteCronJob", _HostUser(JobsDashboardPermissions.Read), $"?id={Guid.NewGuid()}"))
            .Should()
            .Be(StatusCodes.Status403Forbidden);
        (
            await _InvokeAsync(
                app,
                "_RunCronJobOnDemand",
                _HostUser(JobsDashboardPermissions.Read),
                $"?id={Guid.NewGuid()}"
            )
        )
            .Should()
            .Be(StatusCodes.Status403Forbidden);

        await cronManager.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository
            .DidNotReceive()
            .AddOnDemandCronJobOccurrenceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_read_permissions_from_the_configured_claim_type()
    {
        await using var app = _CreateApp(
            new DashboardOptionsBuilder().WithHostAuthentication().WithPermissionClaimType("scope")
        );

        var defaultClaimType = _HostUser(JobsDashboardPermissions.Admin);
        var customClaimType = _HostUser(JobsDashboardPermissions.Admin, claimType: "scope");

        (await _InvokeAsync(app, "StopJobHost", defaultClaimType)).Should().Be(StatusCodes.Status403Forbidden);
        (await _InvokeAsync(app, "StopJobHost", customClaimType)).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_treat_a_legacy_mode_authenticated_caller_as_admin()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithApiKey("secret"));

        // AuthMiddleware stamps this flag after validating the shared credential.
        var status = await _InvokeAsync(
            app,
            "StopJobHost",
            user: null,
            configure: context => context.Items[AuthMiddleware.AuthenticatedKey] = true
        );

        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task should_fail_closed_in_a_legacy_mode_when_the_middleware_did_not_authenticate()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithBasicAuth("admin", "secret"));

        var status = await _InvokeAsync(app, "GetOptions", user: null);

        status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task should_grant_full_access_when_authentication_is_explicitly_disabled()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithNoAuth());

        (await _InvokeAsync(app, "GetOptions", user: null)).Should().Be(StatusCodes.Status200OK);
        (await _InvokeAsync(app, "StopJobHost", user: null)).Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("DeleteTimeJob")]
    [InlineData("CancelJob")]
    public async Task should_reject_a_cross_tenant_row_mutation_without_touching_the_row(string endpointName)
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant: "t-a");
        var jobId = _StoreTimeJob(app, tenantId: "t-b");

        var status = await _InvokeAsync(app, endpointName, _HostUser(JobsDashboardPermissions.Read), $"?id={jobId}");

        status.Should().Be(StatusCodes.Status403Forbidden);
        await app
            .Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>()
            .DidNotReceive()
            .DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await app
            .Services.GetRequiredService<IJobScheduler>()
            .DidNotReceive()
            .CancelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_a_cross_tenant_update_without_reading_the_body()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant: "t-a");
        var jobId = _StoreTimeJob(app, tenantId: "t-b");

        var status = await _InvokeAsync(
            app,
            "UpdateTimeJob",
            _HostUser(JobsDashboardPermissions.Read),
            $"?id={jobId}&timeZoneId=",
            body: """{"tenantId":"t-a"}"""
        );

        status.Should().Be(StatusCodes.Status403Forbidden);
        await app
            .Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>()
            .DidNotReceive()
            .UpdateAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_allow_a_same_tenant_row_mutation_and_pin_the_persisted_tenant()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant: "t-a");
        var jobId = _StoreTimeJob(app, tenantId: "t-a");
        var manager = app.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
        manager
            .UpdateAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new JobResult<TimeJobEntity>(callInfo.Arg<TimeJobEntity>()));

        var status = await _InvokeAsync(
            app,
            "UpdateTimeJob",
            _HostUser(JobsDashboardPermissions.Read),
            $"?id={jobId}&timeZoneId=",
            body: """{"tenantId":"t-b","name":"renamed"}"""
        );

        status.Should().Be(StatusCodes.Status200OK);
        await manager
            .Received(1)
            .UpdateAsync(
                Arg.Is<TimeJobEntity>(job => job.Id == jobId && job.TenantId == "t-a"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_allow_an_admin_to_mutate_any_tenant_row()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant: "t-a");
        var jobId = _StoreTimeJob(app, tenantId: "t-b");
        var scheduler = app.Services.GetRequiredService<IJobScheduler>();
        scheduler.CancelAsync(jobId, Arg.Any<CancellationToken>()).Returns(true);

        var status = await _InvokeAsync(app, "CancelJob", _HostUser(JobsDashboardPermissions.Admin), $"?id={jobId}");

        status.Should().Be(StatusCodes.Status200OK);
        await scheduler.Received(1).CancelAsync(jobId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "t-b")] // missing current tenant never matches a tenant-owned row
    [InlineData("t-a", null)] // system-scope rows are admin-only
    [InlineData(null, null)] // null == null is not a match
    public async Task should_require_a_present_matching_tenant_for_non_admin_row_mutations(
        string? currentTenant,
        string? rowTenant
    )
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant);
        var jobId = _StoreTimeJob(app, rowTenant);

        var status = await _InvokeAsync(app, "DeleteTimeJob", _HostUser(JobsDashboardPermissions.Read), $"?id={jobId}");

        status.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task should_pass_an_unknown_row_through_to_the_existing_not_found_behavior()
    {
        await using var app = _CreateApp(new DashboardOptionsBuilder().WithHostAuthentication(), currentTenant: "t-a");
        var scheduler = app.Services.GetRequiredService<IJobScheduler>();
        var jobId = Guid.NewGuid();
        scheduler.CancelAsync(jobId, Arg.Any<CancellationToken>()).Returns(false);
        // NSubstitute would otherwise auto-create a TimeJobEntity; an unknown id must yield null.
        app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
            .GetTimeJobByIdAsync(jobId, Arg.Any<CancellationToken>())
            .Returns((TimeJobEntity?)null);

        var status = await _InvokeAsync(app, "CancelJob", _HostUser(JobsDashboardPermissions.Read), $"?id={jobId}");

        status.Should().Be(StatusCodes.Status400BadRequest);
    }

    private static ClaimsPrincipal _HostUser(string? permission, string claimType = UserClaimTypes.Permission)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "operator") };
        if (permission is not null)
        {
            claims.Add(new Claim(claimType, permission));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private static Guid _StoreTimeJob(WebApplication app, string? tenantId)
    {
        var jobId = Guid.NewGuid();
        var stored = new TimeJobEntity { Id = jobId, TenantId = tenantId };
        app.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
            .GetTimeJobByIdAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(stored);

        return jobId;
    }

    private static async Task<int> _InvokeAsync(
        WebApplication app,
        string endpointName,
        ClaimsPrincipal? user,
        string queryString = "",
        string? body = null,
        Action<HttpContext>? configure = null
    )
    {
        var endpoint = _Endpoints(app)
            .Single(candidate =>
                string.Equals(
                    candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    endpointName,
                    StringComparison.Ordinal
                )
            );

        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = AbortToken };
        context.Response.Body = new MemoryStream();
        context.Request.QueryString = new QueryString(queryString);
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";
        }

        if (user is not null)
        {
            context.User = user;
        }

        configure?.Invoke(context);
        context.SetEndpoint(endpoint);

        await endpoint.RequestDelegate!(context);

        return context.Response.StatusCode;
    }

    private static WebApplication _CreateApp(DashboardOptionsBuilder config, string? currentTenant = null)
    {
        config.ConfigureDashboardJsonOptions(options => options.PropertyNameCaseInsensitive = true);

        var currentTenantService = Substitute.For<ICurrentTenant>();
        currentTenantService.Id.Returns(currentTenant);
        currentTenantService.IsAvailable.Returns(currentTenant is not null);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddSignalR();
        builder.Services.AddCors();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<JobsDashboardAuthorizer>();
        builder.Services.AddSingleton(currentTenantService);
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(new JobsExecutionContext());
        builder.Services.AddSingleton(new SchedulerOptionsBuilder());
        builder.Services.AddSingleton(Substitute.For<IJobsDashboardRepository<TimeJobEntity, CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<ITimeJobManager<TimeJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<ICronJobManager<CronJobEntity>>());
        builder.Services.AddSingleton(Substitute.For<IJobScheduler>());
        builder.Services.AddSingleton(Substitute.For<IJobsHostScheduler>());

        var app = builder.Build();
        app.MapDashboardEndpoints<TimeJobEntity, CronJobEntity>(config);
        return app;
    }

    private static string _RouteKey(RouteEndpoint endpoint)
    {
        var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.SingleOrDefault() ?? "HUB";
        return $"{method} {endpoint.RoutePattern.RawText}";
    }

    private static IReadOnlyList<Endpoint> _Endpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
    }
}
