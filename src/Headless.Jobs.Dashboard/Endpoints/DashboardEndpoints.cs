using Headless.Dashboard.Authentication;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Hubs;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Headless.Jobs.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints<TTimeJob, TCronJob>(
        this IEndpointRouteBuilder endpoints,
        DashboardOptionsBuilder config
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // New authentication endpoints
        endpoints
            .MapGet("/api/auth/info", _GetAuthInfo)
            .WithName("GetAuthInfo")
            .WithSummary("Get authentication configuration")
            .WithTags("Jobs Dashboard")
            .RequireCors("Jobs_Dashboard_CORS")
            .AllowAnonymous();

        endpoints
            .MapPost("/api/auth/validate", _ValidateAuth)
            .WithName("ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("Jobs Dashboard")
            .RequireCors("Jobs_Dashboard_CORS")
            .AllowAnonymous();

        var apiGroup = endpoints.MapGroup("/api").WithTags("Jobs Dashboard").RequireCors("Jobs_Dashboard_CORS");

        // Apply authentication if configured
        if (config.Auth.Mode == AuthMode.Host)
        {
            // For host authentication, use configured policy or default authorization
            if (!string.IsNullOrEmpty(config.Auth.HostAuthorizationPolicy))
            {
                apiGroup.RequireAuthorization(config.Auth.HostAuthorizationPolicy);
            }
            else
            {
                apiGroup.RequireAuthorization();
            }
        }
        // For other auth modes (Basic, Bearer, Custom), authentication is handled by AuthMiddleware
        // API endpoints are automatically protected when auth is enabled

        // Options endpoint
        apiGroup
            .MapGet("/options", _GetOptions<TTimeJob, TCronJob>)
            .WithName("GetOptions")
            .WithSummary("Get dashboard options and status");

        // Time Jobs endpoints
        apiGroup
            .MapGet("/time-jobs", _GetTimeJobs<TTimeJob, TCronJob>)
            .WithName("GetTimeJobs")
            .WithSummary("Get all time jobs");

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
            .WithSummary("Create chain jobs");

        apiGroup
            .MapPut("/time-job/update", _UpdateTimeJob<TTimeJob, TCronJob>)
            .WithName("UpdateTimeJob")
            .WithSummary("Update time job");

        apiGroup
            .MapDelete("/time-job/delete", _DeleteTimeJob<TTimeJob, TCronJob>)
            .WithName("DeleteTimeJob")
            .WithSummary("Delete time job");

        apiGroup
            .MapDelete("/time-job/delete-batch", _DeleteTimeJobsBatch<TTimeJob, TCronJob>)
            .WithName("DeleteTimeJobsBatch")
            .WithSummary("Delete multiple time jobs");

        // Cron Jobs endpoints
        apiGroup
            .MapGet("/cron-jobs", _GetCronJobs<TTimeJob, TCronJob>)
            .WithName("GetCronJobs")
            .WithSummary("Get all cron jobs");

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
            .MapGet("/cron-job-occurrences/{cronJobId}", _GetCronJobOccurrences<TTimeJob, TCronJob>)
            .WithName("GetCronJobOccurrences")
            .WithSummary("Get cron job occurrences");

        apiGroup
            .MapGet(
                "/cron-job-occurrences/{cronJobId}/paginated",
                _GetCronJobOccurrencesPaginated<TTimeJob, TCronJob>
            )
            .WithName("GetCronJobOccurrencesPaginated")
            .WithSummary("Get paginated cron job occurrences");

        apiGroup
            .MapGet(
                "/cron-job-occurrences/{cronJobId}/graph-data",
                _GetCronJobOccurrencesGraphData<TTimeJob, TCronJob>
            )
            .WithName("GetCronJobOccurrencesGraphData")
            .WithSummary("Get cron job occurrences graph data");

        apiGroup
            .MapPost("/cron-job/add", _AddCronJob<TTimeJob, TCronJob>)
            .WithName("AddCronJob")
            .WithSummary("Add cron job");

        apiGroup
            .MapPut("/cron-job/update", _UpdateCronJob<TTimeJob, TCronJob>)
            .WithName("UpdateCronJob")
            .WithSummary("Update cron job");

        apiGroup
            .MapPost("/cron-job/run", RunCronJobOnDemand<TTimeJob, TCronJob>)
            .WithName("RunCronJobOnDemand")
            .WithSummary("Run cron job on demand");

        apiGroup
            .MapDelete("/cron-job/delete", _DeleteCronJob<TTimeJob, TCronJob>)
            .WithName("DeleteCronJob")
            .WithSummary("Delete cron job");

        apiGroup
            .MapDelete("/cron-job-occurrence/delete", _DeleteCronJobOccurrence<TTimeJob, TCronJob>)
            .WithName("DeleteCronJobOccurrence")
            .WithSummary("Delete cron job occurrence");

        // Job operations
        apiGroup
            .MapPost("/job/cancel", _CancelJob<TTimeJob, TCronJob>)
            .WithName("CancelJob")
            .WithSummary("Cancel job by ID");

        apiGroup
            .MapGet("/job-request/{id}", _GetJobRequest<TTimeJob, TCronJob>)
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
            .WithSummary("Stop job host");

        apiGroup
            .MapPost("/job-host/start", _StartJobHost<TTimeJob, TCronJob>)
            .WithName("StartJobHost")
            .WithSummary("Start job host");

        apiGroup
            .MapPost("/job-host/restart", _RestartJobHost<TTimeJob, TCronJob>)
            .WithName("RestartJobHost")
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

        // SignalR Hub - authentication handled in hub OnConnectedAsync
        endpoints.MapHub<JobsNotificationHub>($"/job-notification-hub").AllowAnonymous();
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
        var authResult = await authService.AuthenticateAsync(context);

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
                currentMachine = schedulerOptions.NodeIdentifier,
                executionContext.LastHostExceptionMessage,
                schedulerTimeZone = schedulerOptions.SchedulerTimeZone?.Id,
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetTimeJobs<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository.GetTimeJobsAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
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
        var result = await repository.GetTimeJobsPaginatedAsync(pageNumber, pageSize, cancellationToken);
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
        var result = await repository.GetTimeJobsGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
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
        var result = await repository.GetTimeJobFullDataAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
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
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var chainRoot = JsonSerializer.Deserialize<TTimeJob>(jsonString, dashboardOptions.DashboardJsonOptions);

        if (chainRoot?.ExecutionTime is { } executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            chainRoot.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        var result = await timeJobsManager.AddAsync(chainRoot!, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Chain jobs created successfully" : "Failed to create chain jobs",
                jobId = result.Result?.Id,
            },
            dashboardOptions.DashboardJsonOptions
        );
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
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var timeJob = JsonSerializer.Deserialize<TTimeJob>(jsonString, dashboardOptions.DashboardJsonOptions)!;

        // Ensure the ID matches
        timeJob.Id = id;

        if (timeJob.ExecutionTime is { } executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            timeJob.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        var result = await timeJobsManager.UpdateAsync(timeJob, cancellationToken);

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
        var result = await timeJobsManager.DeleteAsync(id, cancellationToken);

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
        var idList = ids is { Length: > 0 } ? new List<Guid>(ids) : new List<Guid>();
        var result = await timeJobsManager.DeleteBatchAsync(idList, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time jobs deleted successfully" : "Failed to delete time jobs",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetCronJobs<TTimeJob, TCronJob>(
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository.GetCronJobsAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
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
        var result = await repository.GetCronJobsPaginatedAsync(pageNumber, pageSize, cancellationToken);
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
        var result = await repository.GetCronJobsGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
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
        var result = await repository.GetCronJobsGraphSpecificDataByIdAsync(
            id,
            pastDays,
            futureDays,
            cancellationToken
        );
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
        var result = await repository.GetCronJobFullDataAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronJobOccurrences<TTimeJob, TCronJob>(
        Guid cronJobId,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var result = await repository.GetCronJobsOccurrencesAsync(cronJobId, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
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
        var result = await repository.GetCronJobsOccurrencesPaginatedAsync(
            cronJobId,
            pageNumber,
            pageSize,
            cancellationToken
        );
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
        var result = await repository.GetCronJobsOccurrencesGraphDataAsync(cronJobId, cancellationToken);
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
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var cronJob = JsonSerializer.Deserialize<TCronJob>(jsonString, dashboardOptions.DashboardJsonOptions)!;

        var result = await cronJobsManager.AddAsync(cronJob, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron job added successfully" : "Failed to add cron job",
                jobId = result.Result?.Id,
            },
            dashboardOptions.DashboardJsonOptions
        );
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
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var cronJob = JsonSerializer.Deserialize<TCronJob>(jsonString, dashboardOptions.DashboardJsonOptions)!;

        // Ensure the ID matches
        cronJob.Id = id;

        var result = await cronJobsManager.UpdateAsync(cronJob, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron job updated successfully" : "Failed to update cron job",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> RunCronJobOnDemand<TTimeJob, TCronJob>(
        Guid id,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        await repository.AddOnDemandCronJobOccurrenceAsync(id, cancellationToken);
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
        var result = await cronJobsManager.DeleteAsync(id, cancellationToken);

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
        await repository.DeleteCronJobOccurrenceByIdAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static IResult _CancelJob<TTimeJob, TCronJob>(
        Guid id,
        IJobsDashboardRepository<TTimeJob, TCronJob> repository
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        if (repository.CancelJobById(id))
        {
            return Results.Ok();
        }

        return Results.BadRequest();
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
        var resultData = await repository.GetJobRequestByIdAsync(jobId, jobType, cancellationToken);

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
        await scheduler.StopAsync();
        return Results.Ok();
    }

    private static async Task<IResult> _StartJobHost<TTimeJob, TCronJob>(IJobsHostScheduler scheduler)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        await scheduler.StartAsync();
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
        var jobStatuses = await repository.GetLastWeekJobStatusesAsync(cancellationToken);
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
        var jobStatuses = await repository.GetOverallJobStatusesAsync(cancellationToken);
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
        var machineJobs = await repository.GetMachineJobsAsync(cancellationToken);
        return Results.Json(
            machineJobs.Select(x => new { item1 = x.Item1, item2 = x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    #endregion
}
