// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Jobs.Authorization;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Hubs;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Endpoints;

internal static class DashboardEndpoints
{
    internal static void MapDashboardEndpoints<TTimeJob, TCronJob>(
        this IEndpointRouteBuilder endpoints,
        DashboardOptionsBuilder config
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // Authentication bootstrap routes: explicitly anonymous so the SPA can discover the mode and log in.
        endpoints
            .MapGet("/api/auth/info", _GetAuthInfo)
            .WithName("GetAuthInfo")
            .WithSummary("Get authentication configuration")
            .WithTags("Jobs Dashboard")
            .RequireCors("HeadlessJobsDashboardCORS")
            .AllowAnonymous()
            .WithAccess(DashboardAccess.Anonymous);

        endpoints
            .MapPost("/api/auth/validate", _ValidateAuth)
            .WithName("ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("Jobs Dashboard")
            .RequireCors("HeadlessJobsDashboardCORS")
            .AllowAnonymous()
            .WithAccess(DashboardAccess.Anonymous);

        var apiGroup = endpoints.MapGroup("/api").WithTags("Jobs Dashboard").RequireCors("HeadlessJobsDashboardCORS");

        // Authentication gate per mode. Host: the host's authorization middleware (default or named policy) rejects
        // unauthenticated callers with 401 before any handler runs. Basic / ApiKey / Custom: AuthMiddleware does the
        // same for every /api request. Permission enforcement (read / tenant-row / admin) is layered on top by the
        // group filter below, which is the single decision point shared with the SignalR hub.
        if (config.Auth.Mode == AuthMode.Host)
        {
            if (!string.IsNullOrEmpty(config.Auth.HostAuthorizationPolicy))
            {
                apiGroup.RequireAuthorization(config.Auth.HostAuthorizationPolicy);
            }
            else
            {
                apiGroup.RequireAuthorization();
            }
        }

        apiGroup.AddEndpointFilter(
            static (context, next) =>
                context
                    .HttpContext.RequestServices.GetRequiredService<JobsDashboardAuthorizer>()
                    .EnforceAsync(context, next)
        );

        // Options endpoint
        apiGroup
            .MapGet("/options", _GetOptions<TTimeJob, TCronJob>)
            .WithName("GetOptions")
            .WithSummary("Get dashboard options and status")
            .WithAccess(DashboardAccess.Read);

        // Time Jobs endpoints
        apiGroup
            .MapGet("/time-jobs/paginated", _GetTimeJobsPaginated<TTimeJob, TCronJob>)
            .WithName("GetTimeJobsPaginated")
            .WithSummary("Get paginated time jobs")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/time-jobs/graph-data-range", _GetTimeJobsGraphDataRange<TTimeJob, TCronJob>)
            .WithName("GetTimeJobsGraphDataRange")
            .WithSummary("Get time jobs graph data for specific date range")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/time-jobs/graph-data", _GetTimeJobsGraphData<TTimeJob, TCronJob>)
            .WithName("GetTimeJobsGraphData")
            .WithSummary("Get time jobs graph data")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapPost("/time-job/add", _CreateChainJobs<TTimeJob, TCronJob>)
            .WithName("CreateChainJobs")
            .WithSummary("Create chain jobs")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes))
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapPut("/time-job/update", _UpdateTimeJob<TTimeJob, TCronJob>)
            .WithName("UpdateTimeJob")
            .WithSummary("Update time job")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes))
            .WithAccess(DashboardAccess.TenantRowMutation);

        apiGroup
            .MapDelete("/time-job/delete", _DeleteTimeJob<TTimeJob, TCronJob>)
            .WithName("DeleteTimeJob")
            .WithSummary("Delete time job")
            .WithAccess(DashboardAccess.TenantRowMutation);

        apiGroup
            .MapDelete("/time-job/delete-batch", _DeleteTimeJobsBatch<TTimeJob, TCronJob>)
            .WithName("DeleteTimeJobsBatch")
            .WithSummary("Delete multiple time jobs")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes))
            .WithAccess(DashboardAccess.Admin);

        // Cron Jobs endpoints — cron definitions and occurrences are system scope, so every mutation is admin-only.
        apiGroup
            .MapGet("/cron-jobs/paginated", _GetCronJobsPaginated<TTimeJob, TCronJob>)
            .WithName("GetCronJobsPaginated")
            .WithSummary("Get paginated cron jobs")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/cron-jobs/graph-data-range", _GetCronJobsGraphDataRange<TTimeJob, TCronJob>)
            .WithName("GetCronJobsGraphDataRange")
            .WithSummary("Get cron jobs graph data for specific date range")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/cron-jobs/graph-data-range-id", _GetCronJobsByIdGraphDataRange<TTimeJob, TCronJob>)
            .WithName("GetCronJobsByIdGraphDataRange")
            .WithSummary("Get cron job graph data by ID for specific date range")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/cron-jobs/graph-data", _GetCronJobsGraphData<TTimeJob, TCronJob>)
            .WithName("GetCronJobsGraphData")
            .WithSummary("Get cron jobs graph data")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/cron-job-occurrences/{cronJobId}/paginated", _GetCronJobOccurrencesPaginated<TTimeJob, TCronJob>)
            .WithName("GetCronJobOccurrencesPaginated")
            .WithSummary("Get paginated cron job occurrences")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/cron-job-occurrences/{cronJobId}/graph-data", _GetCronJobOccurrencesGraphData<TTimeJob, TCronJob>)
            .WithName("GetCronJobOccurrencesGraphData")
            .WithSummary("Get cron job occurrences graph data")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapPost("/cron-job/add", _AddCronJob<TTimeJob, TCronJob>)
            .WithName("AddCronJob")
            .WithSummary("Add cron job")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes))
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapPut("/cron-job/update", _UpdateCronJob<TTimeJob, TCronJob>)
            .WithName("UpdateCronJob")
            .WithSummary("Update cron job")
            .WithMetadata(new RequestSizeLimitAttribute(DashboardOptionsBuilder.MaxRequestBodyBytes))
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapPost("/cron-job/run", _RunCronJobOnDemand<TTimeJob, TCronJob>)
            .WithName("_RunCronJobOnDemand")
            .WithSummary("Run cron job on demand")
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapDelete("/cron-job/delete", _DeleteCronJob<TTimeJob, TCronJob>)
            .WithName("DeleteCronJob")
            .WithSummary("Delete cron job")
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapDelete("/cron-job-occurrence/delete", _DeleteCronJobOccurrence<TTimeJob, TCronJob>)
            .WithName("DeleteCronJobOccurrence")
            .WithSummary("Delete cron job occurrence")
            .WithAccess(DashboardAccess.Admin);

        // Job operations
        apiGroup
            .MapPost("/job/cancel", CancelJobAsync<TTimeJob, TCronJob>)
            .WithName("CancelJob")
            .WithSummary("Cancel job by ID")
            .WithAccess(DashboardAccess.TenantRowMutation);

        // Literal "id" segment (not a route parameter): the SPA calls "job-request/id" and supplies
        // jobId + jobType via query string, which _GetJobRequest binds. Avoids a dead {id} route token.
        apiGroup
            .MapGet("/job-request/id", _GetJobRequest<TTimeJob, TCronJob>)
            .WithName("GetJobRequest")
            .WithSummary("Get job request by ID")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/job-functions", _GetJobFunctions<TTimeJob, TCronJob>)
            .WithName("GetJobFunctions")
            .WithSummary("Get available job functions")
            .WithAccess(DashboardAccess.Read);

        // Host operations
        apiGroup
            .MapGet("/job-host/next-job", _GetNextJob<TTimeJob, TCronJob>)
            .WithName("GetNextJob")
            .WithSummary("Get next planned job")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapPost("/job-host/stop", _StopJobHost<TTimeJob, TCronJob>)
            .WithName("StopJobHost")
            .WithSummary("Stop job host")
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapPost("/job-host/start", _StartJobHost<TTimeJob, TCronJob>)
            .WithName("StartJobHost")
            .WithSummary("Start job host")
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapPost("/job-host/restart", _RestartJobHost<TTimeJob, TCronJob>)
            .WithName("RestartJobHost")
            .WithSummary("Restart job host")
            .WithAccess(DashboardAccess.Admin);

        apiGroup
            .MapGet("/job-host/status", _GetJobHostStatus<TTimeJob, TCronJob>)
            .WithName("GetJobHostStatus")
            .WithSummary("Get job host status")
            .WithAccess(DashboardAccess.Read);

        // Statistics endpoints
        apiGroup
            .MapGet("/job/statuses/get-last-week", _GetLastWeekJobStatus<TTimeJob, TCronJob>)
            .WithName("GetLastWeekJobStatus")
            .WithSummary("Get last week job statuses")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/job/statuses/get", _GetJobStatuses<TTimeJob, TCronJob>)
            .WithName("GetJobStatuses")
            .WithSummary("Get overall job statuses")
            .WithAccess(DashboardAccess.Read);

        apiGroup
            .MapGet("/job/machine/jobs", _GetMachineJobs<TTimeJob, TCronJob>)
            .WithName("GetMachineJobs")
            .WithSummary("Get machine jobs")
            .WithAccess(DashboardAccess.Read);

        // Live nodes (coordination membership liveness snapshot)
        apiGroup
            .MapGet("/nodes", _GetLiveNodes<TTimeJob, TCronJob>)
            .WithName("GetLiveNodes")
            .WithSummary("Get live cluster nodes from the coordination membership substrate")
            .WithAccess(DashboardAccess.Read);

        // SignalR hub: authentication + read-permission check happen in JobsNotificationHub.OnConnectedAsync
        // (the hub path is outside /api, so neither AuthMiddleware nor the group filter covers it).
        endpoints
            .MapHub<JobsNotificationHub>("/job-notification-hub")
            .AllowAnonymous()
            .WithAccess(DashboardAccess.Read);
    }

    #region Endpoint Handlers

    private static IResult _GetAuthInfo(IAuthService authService, DashboardOptionsBuilder dashboardOptions)
    {
        var authInfo = authService.GetAuthInfo();

        // Return in format expected by frontend
        var response = new
        {
            mode = authInfo.Mode.ToString().ToLower(CultureInfo.InvariantCulture),
            enabled = authInfo.IsEnabled,
            sessionTimeout = authInfo.SessionTimeoutMinutes,
        };

        return Results.Json(response, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _ValidateAuth(
        HttpContext context,
        IAuthService authService,
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
        string timeZoneId,
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

        if (chainRoot?.ExecutionTime is { } executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            chainRoot.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
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

    private static async Task<IResult> _UpdateTimeJob<TTimeJob, TCronJob>(
        Guid id,
        HttpContext context,
        ITimeJobManager<TTimeJob> timeJobsManager,
        IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
        JobsDashboardAuthorizer authorizer,
        DashboardOptionsBuilder dashboardOptions,
        string timeZoneId,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (stored, forbidden) = await _AuthorizeTimeJobRowAsync(
                id,
                context,
                persistenceProvider,
                authorizer,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (timeJob, bodyError) = await DashboardRequestBodyReader
            .ReadAsync<TTimeJob>(context, dashboardOptions.DashboardJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (bodyError is not null)
        {
            return bodyError;
        }

        // Ensure the ID matches, and pin the tenant to the persisted value: the body can never move a job between
        // tenants (the manager preserves the stored root tenant too — this keeps the boundary explicit here).
        timeJob!.Id = id;
        timeJob.TenantId = stored?.TenantId;

        if (timeJob.ExecutionTime is { } executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
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
        HttpContext context,
        ITimeJobManager<TTimeJob> timeJobsManager,
        IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
        JobsDashboardAuthorizer authorizer,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (_, forbidden) = await _AuthorizeTimeJobRowAsync(
                id,
                context,
                persistenceProvider,
                authorizer,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

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

    internal static async Task<IResult> CancelJobAsync<TTimeJob, TCronJob>(
        Guid id,
        HttpContext context,
        IJobScheduler scheduler,
        IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
        JobsDashboardAuthorizer authorizer,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var (_, forbidden) = await _AuthorizeTimeJobRowAsync(
                id,
                context,
                persistenceProvider,
                authorizer,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (forbidden is not null)
        {
            return forbidden;
        }

        if (await scheduler.CancelAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return Results.Ok();
        }

        return Results.BadRequest();
    }

    /// <summary>
    /// Loads the persisted time job and authorizes the caller against its stored tenant. The stored row is the only
    /// authority — request bodies and query values never participate. A missing row passes through so the existing
    /// not-found behavior of the manager / scheduler is preserved (no state can change for an unknown id).
    /// </summary>
    private static async Task<(TTimeJob? Stored, IResult? Forbidden)> _AuthorizeTimeJobRowAsync<TTimeJob, TCronJob>(
        Guid id,
        HttpContext context,
        IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
        JobsDashboardAuthorizer authorizer,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var stored = await persistenceProvider.GetTimeJobByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return (null, null);
        }

        var caller = authorizer.Resolve(context);

        return JobsDashboardAuthorizer.CanMutateTimeJob(context, caller, stored.TenantId)
            ? (stored, null)
            : (stored, Results.StatusCode(StatusCodes.Status403Forbidden));
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
