// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Endpoints;

internal static class DashboardEndpoints
{
    internal const string CorsPolicyName = "HeadlessJobsDashboardCORS";

    internal static void MapDashboardEndpoints<TTimeJob, TCronJob>(
        this IEndpointRouteBuilder endpoints,
        DashboardOptionsBuilder config
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // The dashboard SPA is the only client of these endpoints, so every one is excluded from API descriptions.
        var applyCors = config.CorsPolicyBuilder is not null;

        const string authenticationName = DashboardOptionsBuilder.AuthenticationName;

        // The shared sign-in endpoints (auth/sign-in, api/auth/info, api/auth/session, api/auth/sign-out). Mapping them
        // also moves the host-mode API and hub below onto the dashboard's session cookie. The empty-prefix group only
        // carries the CORS policy that Basic and API key dashboards may use.
        endpoints
            .MapGroup(string.Empty)
            .RequireDashboardCors(CorsPolicyName, applyCors)
            .MapDashboardAuthEndpoints(authenticationName);

        endpoints
            .MapPost("/api/auth/validate", _ValidateAuth)
            .WithName("ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("Jobs Dashboard")
            .RequireDashboardCors(CorsPolicyName, applyCors)
            .AllowAnonymous()
            .ExcludeFromDescription();

        var apiGroup = endpoints
            .MapGroup("/api")
            .WithTags("Jobs Dashboard")
            .RequireDashboardCors(CorsPolicyName, applyCors)
            .ExcludeFromDescription()
            .RequireDashboardAuthentication(authenticationName)
            .RequireDashboardRequestIntegrity();

        // Options endpoint
        apiGroup
            .MapGet("/options", _GetOptions<TTimeJob, TCronJob>)
            .WithName("GetOptions")
            .WithSummary("Get dashboard options and status");

        // Time Jobs endpoints
        apiGroup
            .MapGet("/time-jobs/paginated", _GetTimeJobsPaginated<TTimeJob, TCronJob>)
            .WithName("GetTimeJobsPaginated")
            .WithSummary("Get paginated time jobs");

        apiGroup
            .MapGet("/time-jobs/graph-data-range", _GetTimeJobsGraphDataRange<TTimeJob, TCronJob>)
            .WithName("GetTimeJobsGraphDataRange")
            .WithSummary("Get time jobs graph data for specific date range");

        apiGroup
            .MapGet("/time-jobs/graph-data", _GetTimeJobsGraphData<TTimeJob, TCronJob>)
            .WithName("GetTimeJobsGraphData")
            .WithSummary("Get time jobs graph data");

        apiGroup
            .MapPost("/time-job/add", _CreateChainJobs<TTimeJob, TCronJob>)
            .WithName("CreateChainJobs")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Create chain jobs")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes));

        apiGroup
            .MapPut("/time-job/update", _UpdateTimeJob<TTimeJob, TCronJob>)
            .WithName("UpdateTimeJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Update time job")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes));

        apiGroup
            .MapDelete("/time-job/delete", _DeleteTimeJob<TTimeJob, TCronJob>)
            .WithName("DeleteTimeJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Delete time job");

        apiGroup
            .MapDelete("/time-job/delete-batch", _DeleteTimeJobsBatch<TTimeJob, TCronJob>)
            .WithName("DeleteTimeJobsBatch")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Delete multiple time jobs")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes));

        // Cron Jobs endpoints
        apiGroup
            .MapGet("/cron-jobs/paginated", _GetCronJobsPaginated<TTimeJob, TCronJob>)
            .WithName("GetCronJobsPaginated")
            .WithSummary("Get paginated cron jobs");

        apiGroup
            .MapGet("/cron-jobs/graph-data-range", _GetCronJobsGraphDataRange<TTimeJob, TCronJob>)
            .WithName("GetCronJobsGraphDataRange")
            .WithSummary("Get cron jobs graph data for specific date range");

        apiGroup
            .MapGet("/cron-jobs/graph-data-range-id", _GetCronJobsByIdGraphDataRange<TTimeJob, TCronJob>)
            .WithName("GetCronJobsByIdGraphDataRange")
            .WithSummary("Get cron job graph data by ID for specific date range");

        apiGroup
            .MapGet("/cron-jobs/graph-data", _GetCronJobsGraphData<TTimeJob, TCronJob>)
            .WithName("GetCronJobsGraphData")
            .WithSummary("Get cron jobs graph data");

        apiGroup
            .MapGet("/cron-job-occurrences/{cronJobId}/paginated", _GetCronJobOccurrencesPaginated<TTimeJob, TCronJob>)
            .WithName("GetCronJobOccurrencesPaginated")
            .WithSummary("Get paginated cron job occurrences");

        apiGroup
            .MapGet("/cron-job-occurrences/{cronJobId}/graph-data", _GetCronJobOccurrencesGraphData<TTimeJob, TCronJob>)
            .WithName("GetCronJobOccurrencesGraphData")
            .WithSummary("Get cron job occurrences graph data");

        apiGroup
            .MapPost("/cron-job/add", _AddCronJob<TTimeJob, TCronJob>)
            .WithName("AddCronJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Add cron job")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes));

        apiGroup
            .MapPut("/cron-job/update", _UpdateCronJob<TTimeJob, TCronJob>)
            .WithName("UpdateCronJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Update cron job")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes));

        apiGroup
            .MapPost("/cron-job/run", _RunCronJobOnDemand<TTimeJob, TCronJob>)
            .WithName("_RunCronJobOnDemand")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Run cron job on demand");

        apiGroup
            .MapDelete("/cron-job/delete", _DeleteCronJob<TTimeJob, TCronJob>)
            .WithName("DeleteCronJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Delete cron job");

        apiGroup
            .MapDelete("/cron-job-occurrence/delete", _DeleteCronJobOccurrence<TTimeJob, TCronJob>)
            .WithName("DeleteCronJobOccurrence")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Delete cron job occurrence");

        apiGroup
            .MapPost("/cron-job-occurrence/requeue", RequeueCronJobOccurrenceAsync)
            .WithName("RequeueCronJobOccurrence")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Requeue a failed cron job occurrence");

        // Job operations
        apiGroup
            .MapPost("/job/cancel", CancelJobAsync)
            .WithName("CancelJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Cancel job by ID");
        apiGroup
            .MapPost("/job/requeue", RequeueJobAsync)
            .WithName("RequeueJob")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Requeue a failed time job by ID");

        // Literal "id" segment (not a route parameter): the SPA calls "job-request/id" and supplies
        // jobId + jobType via query string, which _GetJobRequest binds. Avoids a dead {id} route token.
        apiGroup
            .MapGet("/job-request/id", _GetJobRequest<TTimeJob, TCronJob>)
            .WithName("GetJobRequest")
            .WithSummary("Get job request by ID");

        apiGroup
            .MapGet("/job-functions", _GetJobFunctions<TTimeJob, TCronJob>)
            .WithName("GetJobFunctions")
            .WithSummary("Get available job functions");

        // Host operations
        apiGroup
            .MapGet("/job-host/next-job", _GetNextJob<TTimeJob, TCronJob>)
            .WithName("GetNextJob")
            .WithSummary("Get next planned job");

        apiGroup
            .MapPost("/job-host/stop", _StopJobHost<TTimeJob, TCronJob>)
            .WithName("StopJobHost")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Stop job host");

        apiGroup
            .MapPost("/job-host/start", _StartJobHost<TTimeJob, TCronJob>)
            .WithName("StartJobHost")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Start job host");

        apiGroup
            .MapPost("/job-host/restart", _RestartJobHost<TTimeJob, TCronJob>)
            .WithName("RestartJobHost")
            .RequireDashboardOperation(authenticationName)
            .WithSummary("Restart job host");

        apiGroup
            .MapGet("/job-host/status", _GetJobHostStatus<TTimeJob, TCronJob>)
            .WithName("GetJobHostStatus")
            .WithSummary("Get job host status");

        // Statistics endpoints
        apiGroup
            .MapGet("/job/statuses/get-last-week", _GetLastWeekJobStatus<TTimeJob, TCronJob>)
            .WithName("GetLastWeekJobStatus")
            .WithSummary("Get last week job statuses");

        apiGroup
            .MapGet("/job/statuses/get", _GetJobStatuses<TTimeJob, TCronJob>)
            .WithName("GetJobStatuses")
            .WithSummary("Get overall job statuses");

        apiGroup
            .MapGet("/job/machine/jobs", _GetMachineJobs<TTimeJob, TCronJob>)
            .WithName("GetMachineJobs")
            .WithSummary("Get machine jobs");

        // Live nodes (coordination membership liveness snapshot)
        apiGroup
            .MapGet("/nodes", _GetLiveNodes<TTimeJob, TCronJob>)
            .WithName("GetLiveNodes")
            .WithSummary("Get live cluster nodes from the coordination membership substrate");

        // The hub carries the API's view rule, so the host's UseAuthorization refuses an unauthorized client before
        // SignalR adds its connection to Clients.All, where it would hear a broadcast before OnConnectedAsync ran.
        // Closing on authentication expiry ends a connection whose session lapsed; the SPA reconnects with the current
        // cookie or returns to sign-in.
        var hub = endpoints
            .MapHub<JobsNotificationHub>(
                JobsNotificationHub.Path,
                options => options.CloseOnAuthenticationExpiration = true
            )
            .RequireDashboardCors(CorsPolicyName, applyCors)
            .RequireDashboardAuthentication(authenticationName);

        // A sibling subdomain is same-site, so the browser still sends it the Strict session cookie on a WebSocket
        // handshake; refusing a foreign Origin stops such a page from reading job data over the hub.
        if (config.Auth.Mode == AuthMode.Host)
        {
            hub.Add(_RefuseCrossOriginRequests);
        }
    }

    private static void _RefuseCrossOriginRequests(EndpointBuilder endpoint)
    {
        if (endpoint.RequestDelegate is not { } next)
        {
            return;
        }

        endpoint.RequestDelegate = context =>
        {
            if (_IsSameOriginOrNoOrigin(context.Request))
            {
                return next(context);
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Whether <paramref name="request"/> carries no <c>Origin</c> header, or one naming the request's own scheme,
    /// host, and port. A non-browser client sends no <c>Origin</c>; a browser always sends it on a cross-origin request.
    /// </summary>
    private static bool _IsSameOriginOrNoOrigin(HttpRequest request)
    {
        var origins = request.Headers.Origin;
        if (origins.Count == 0)
        {
            return true;
        }

        if (
            origins.Count != 1
            || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || !request.Host.HasValue
        )
        {
            return false;
        }

        var requestPort =
            request.Host.Port
            ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        return string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(origin.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == requestPort;
    }

    #region Endpoint Handlers

    private static async Task<IResult> _ValidateAuth(
        HttpContext context,
        [FromKeyedServices(DashboardOptionsBuilder.AuthenticationName)] IAuthService authService,
        DashboardOptionsBuilder dashboardOptions
    )
    {
        var authResult = await authService.AuthenticateAsync(context).ConfigureAwait(false);

        if (authResult.IsAuthenticated)
        {
            return Results.Json(
                new
                {
                    authenticated = true,
                    username = authResult.Username,
                    message = "Authentication successful",
                },
                dashboardOptions.DashboardJsonOptions
            );
        }

        return Results.Unauthorized();
    }

    private static IResult _GetOptions<TTimeJob, TCronJob>(
        JobsExecutionContext executionContext,
        SchedulerOptionsBuilder schedulerOptions,
        DashboardOptionsBuilder dashboardOptions
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        return Results.Json(
            new
            {
                maxConcurrency = schedulerOptions.MaxConcurrency,
                schedulerOptions.IdleWorkerTimeOut,
                currentMachine = schedulerOptions.NodeId,
                executionContext.LastHostExceptionMessage,
                schedulerTimeZone = schedulerOptions.SchedulerTimeZone?.Id,
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetTimeJobsPaginated<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var paginationError = _GetPaginationError(pageNumber, pageSize);
        if (paginationError is not null)
        {
            return paginationError;
        }

        var result = await repository
            .GetTimeJobsPaginatedAsync(pageNumber, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetTimeJobsGraphDataRange<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository
            .GetTimeJobsGraphSpecificDataAsync(pastDays, futureDays, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetTimeJobsGraphData<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository.GetTimeJobFullDataAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            result.Select(x => new { item1 = x.Status, item2 = x.Count }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _CreateChainJobs<TTimeJob, TCronJob>(
        HttpContext context,
        ITimeJobManager<TTimeJob> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        string? timeZoneId,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (chainRoot, bodyError) = await DashboardRequestBodyReader
            .ReadAsync<TTimeJob>(context, dashboardOptions.DashboardJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (bodyError is not null)
        {
            return bodyError;
        }

        if (chainRoot is not null && !string.IsNullOrEmpty(timeZoneId))
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
            {
                return _UnknownTimeZone();
            }

            _ConvertChainTimesToUtc(chainRoot, timeZone);
        }

        // AddAsync returns the persisted entity and throws on failure; the dashboard reports it as success/failure data.
        try
        {
            var created = await timeJobsManager.AddAsync(chainRoot!, cancellationToken).ConfigureAwait(false);

            return Results.Json(
                new
                {
                    success = true,
                    message = "Chain jobs created successfully",
                    jobId = (Guid?)created.Id,
                },
                dashboardOptions.DashboardJsonOptions
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Results.Json(
                new
                {
                    success = false,
                    message = "Failed to create chain jobs",
                    jobId = (Guid?)null,
                },
                dashboardOptions.DashboardJsonOptions
            );
        }
    }

    // A dashboard time without an offset is a wall-clock time in the caller's zone, for the root and every step of a
    // chain alike. A time that already carries Z or an offset is an instant and is kept as sent.
    private static void _ConvertChainTimesToUtc<TTimeJob>(TTimeJob job, TimeZoneInfo timeZone)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        if (job.ExecutionTime is { } executionTime)
        {
            job.ExecutionTime = executionTime.Kind switch
            {
                DateTimeKind.Unspecified => DateTime.SpecifyKind(
                    TimeZoneInfo.ConvertTimeToUtc(executionTime, timeZone),
                    DateTimeKind.Utc
                ),
                DateTimeKind.Local => executionTime.ToUniversalTime(),
                _ => executionTime,
            };
        }

        foreach (var child in job.Children)
        {
            _ConvertChainTimesToUtc(child, timeZone);
        }
    }

    private static async Task<IResult> _UpdateTimeJob<TTimeJob, TCronJob>(
        Guid id,
        HttpContext context,
        ITimeJobManager<TTimeJob> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        string timeZoneId,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (timeJob, bodyError) = await DashboardRequestBodyReader
            .ReadAsync<TTimeJob>(context, dashboardOptions.DashboardJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (bodyError is not null)
        {
            return bodyError;
        }

        // Ensure the ID matches
        timeJob!.Id = id;

        if (timeJob.ExecutionTime is { } executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var tz))
            {
                return _UnknownTimeZone();
            }

            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            timeJob.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        var result = await timeJobsManager.UpdateAsync(timeJob, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time job updated successfully" : "Failed to update time job",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _DeleteTimeJob<TTimeJob, TCronJob>(
        Guid id,
        ITimeJobManager<TTimeJob> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await timeJobsManager.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time job deleted successfully" : "Failed to delete time job",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _DeleteTimeJobsBatch<TTimeJob, TCronJob>(
        [FromBody] Guid[] ids,
        ITimeJobManager<TTimeJob> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        if (!IsValidBatchSize(ids.Length))
        {
            return Results.BadRequest(
                $"A batch may contain at most {DashboardOptionsBuilder.MaxBatchDeleteIds} job IDs."
            );
        }

        var idList = ids is { Length: > 0 } ? new List<Guid>(ids) : [];
        var result = await timeJobsManager.DeleteBatchAsync(idList, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time jobs deleted successfully" : "Failed to delete time jobs",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetCronJobsPaginated<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var paginationError = _GetPaginationError(pageNumber, pageSize);
        if (paginationError is not null)
        {
            return paginationError;
        }

        var result = await repository
            .GetCronJobsPaginatedAsync(pageNumber, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronJobsGraphDataRange<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository
            .GetCronJobsGraphSpecificDataAsync(pastDays, futureDays, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronJobsByIdGraphDataRange<TTimeJob, TCronJob>(
        Guid id,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository
            .GetCronJobsGraphSpecificDataByIdAsync(id, pastDays, futureDays, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronJobsGraphData<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository.GetCronJobFullDataAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            result.Select(x => new { item1 = x.Status, item2 = x.Count }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetCronJobOccurrencesPaginated<TTimeJob, TCronJob>(
        Guid cronJobId,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var paginationError = _GetPaginationError(pageNumber, pageSize);
        if (paginationError is not null)
        {
            return paginationError;
        }

        var result = await repository
            .GetCronJobsOccurrencesPaginatedAsync(cronJobId, pageNumber, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronJobOccurrencesGraphData<TTimeJob, TCronJob>(
        Guid cronJobId,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository
            .GetCronJobsOccurrencesGraphDataAsync(cronJobId, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _AddCronJob<TTimeJob, TCronJob>(
        HttpContext context,
        ICronJobManager<TCronJob> cronJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (cronJob, bodyError) = await DashboardRequestBodyReader
            .ReadAsync<TCronJob>(context, dashboardOptions.DashboardJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (bodyError is not null)
        {
            return bodyError;
        }

        // AddAsync returns the persisted entity and throws on failure; the dashboard reports it as success/failure data.
        try
        {
            var created = await cronJobsManager.AddAsync(cronJob!, cancellationToken).ConfigureAwait(false);

            return Results.Json(
                new
                {
                    success = true,
                    message = "Cron job added successfully",
                    jobId = (Guid?)created.Id,
                },
                dashboardOptions.DashboardJsonOptions
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Results.Json(
                new
                {
                    success = false,
                    message = "Failed to add cron job",
                    jobId = (Guid?)null,
                },
                dashboardOptions.DashboardJsonOptions
            );
        }
    }

    private static async Task<IResult> _UpdateCronJob<TTimeJob, TCronJob>(
        Guid id,
        HttpContext context,
        ICronJobManager<TCronJob> cronJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (cronJob, bodyError) = await DashboardRequestBodyReader
            .ReadAsync<TCronJob>(context, dashboardOptions.DashboardJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (bodyError is not null)
        {
            return bodyError;
        }

        // Ensure the ID matches
        cronJob!.Id = id;

        var result = await cronJobsManager.UpdateAsync(cronJob, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron job updated successfully" : "Failed to update cron job",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _RunCronJobOnDemand<TTimeJob, TCronJob>(
        Guid id,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        await repository.AddOnDemandCronJobOccurrenceAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok();
    }

    private static async Task<IResult> _DeleteCronJob<TTimeJob, TCronJob>(
        Guid id,
        ICronJobManager<TCronJob> cronJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await cronJobsManager.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron job deleted successfully" : "Failed to delete cron job",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _DeleteCronJobOccurrence<TTimeJob, TCronJob>(
        Guid id,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        await repository.DeleteCronJobOccurrenceByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok();
    }

    internal static async Task<IResult> CancelJobAsync(
        Guid id,
        IJobScheduler scheduler,
        CancellationToken cancellationToken
    )
    {
        if (await scheduler.CancelAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return Results.Ok();
        }

        return Results.BadRequest();
    }

    internal static async Task<IResult> RequeueJobAsync(
        Guid id,
        IJobScheduler scheduler,
        CancellationToken cancellationToken
    )
    {
        return _RequeueResult(await scheduler.RequeueAsync(id, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<IResult> RequeueCronJobOccurrenceAsync(
        Guid id,
        IJobScheduler scheduler,
        CancellationToken cancellationToken
    )
    {
        return _RequeueResult(await scheduler.RequeueOccurrenceAsync(id, cancellationToken).ConfigureAwait(false));
    }

    // The refusal name is the body so the dashboard's error alert tells the operator why nothing happened.
    private static IResult _RequeueResult(JobRequeueOutcome outcome)
    {
        return outcome == JobRequeueOutcome.Requeued ? Results.Ok() : Results.BadRequest(outcome.ToString());
    }

    private static async Task<IResult> _GetJobRequest<TTimeJob, TCronJob>(
        Guid jobId,
        JobType jobType,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var resultData = await repository
            .GetJobRequestByIdAsync(jobId, jobType, cancellationToken)
            .ConfigureAwait(false);

        var response = new { Result = resultData.Item1, MatchType = resultData.Item2 };
        return Results.Json(response, dashboardOptions.DashboardJsonOptions);
    }

    private static IResult _GetJobFunctions<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        JobFunctionRegistry functionRegistry,
        DashboardOptionsBuilder dashboardOptions
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = repository
            .GetJobFunctions()
            .Select(x => new
            {
                FunctionName = x.Item1,
                functionRegistry.Descriptors[x.Item1].ContractVersion,
                FunctionRequestNamespace = x.Item2.Item1,
                FunctionRequestType = x.Item2.Item2,
                Priority = (int)x.Item2.Item3,
            });

        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static IResult _GetNextJob<TTimeJob, TCronJob>(
        JobsExecutionContext executionContext,
        DashboardOptionsBuilder dashboardOptions
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = new { NextOccurrence = executionContext.GetNextPlannedOccurrence() };
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _StopJobHost<TTimeJob, TCronJob>(IJobsHostScheduler scheduler)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        await scheduler.StopAsync().ConfigureAwait(false);
        return Results.Ok();
    }

    private static async Task<IResult> _StartJobHost<TTimeJob, TCronJob>(IJobsHostScheduler scheduler)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        await scheduler.StartAsync().ConfigureAwait(false);
        return Results.Ok();
    }

    private static IResult _RestartJobHost<TTimeJob, TCronJob>(IJobsHostScheduler scheduler)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        scheduler.Restart();
        return Results.Ok();
    }

    private static IResult _GetJobHostStatus<TTimeJob, TCronJob>(IJobsHostScheduler scheduler)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        return Results.Ok(new { scheduler.IsRunning });
    }

    private static async Task<IResult> _GetLastWeekJobStatus<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var jobStatuses = await repository.GetLastWeekJobStatusesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetJobStatuses<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var jobStatuses = await repository.GetOverallJobStatusesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetMachineJobs<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var machineJobs = await repository.GetMachineJobsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(
            machineJobs.Select(x => new { item1 = x.Item1, item2 = x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetLiveNodes<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var nodes = await repository.GetLiveNodesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Json(nodes, dashboardOptions.DashboardJsonOptions);
    }

    internal static bool IsValidPagination(int pageNumber, int pageSize)
    {
        return pageNumber > 0 && pageSize is > 0 and <= DashboardOptionsBuilder.MaxPageSize;
    }

    internal static bool IsValidBatchSize(int count)
    {
        return count is >= 0 and <= DashboardOptionsBuilder.MaxBatchDeleteIds;
    }

    private static IResult _UnknownTimeZone()
    {
        return Results.BadRequest("timeZoneId is not a time zone this host knows.");
    }

    private static IResult? _GetPaginationError(int pageNumber, int pageSize)
    {
        return IsValidPagination(pageNumber, pageSize)
            ? null
            : Results.BadRequest(
                $"pageNumber must be positive and pageSize must be between 1 and {DashboardOptionsBuilder.MaxPageSize}."
            );
    }

    #endregion
}
