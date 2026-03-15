using Headless.Jobs.Authentication;
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
    public static void MapDashboardEndpoints<TTimeTicker, TCronTicker>(
        this IEndpointRouteBuilder endpoints,
        DashboardOptionsBuilder config
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
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
            .MapGet("/options", _GetOptions<TTimeTicker, TCronTicker>)
            .WithName("GetOptions")
            .WithSummary("Get dashboard options and status");

        // Time Jobs endpoints
        apiGroup
            .MapGet("/time-jobs", _GetTimeTickers<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickers")
            .WithSummary("Get all time tickers");

        apiGroup
            .MapGet("/time-jobs/paginated", _GetTimeTickersPaginated<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickersPaginated")
            .WithSummary("Get paginated time tickers");

        apiGroup
            .MapGet("/time-jobs/graph-data-range", _GetTimeTickersGraphDataRange<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickersGraphDataRange")
            .WithSummary("Get time tickers graph data for specific date range");

        apiGroup
            .MapGet("/time-jobs/graph-data", _GetTimeTickersGraphData<TTimeTicker, TCronTicker>)
            .WithName("GetTimeTickersGraphData")
            .WithSummary("Get time tickers graph data");

        apiGroup
            .MapPost("/time-job/add", _CreateChainJobs<TTimeTicker, TCronTicker>)
            .WithName("CreateChainJobs")
            .WithSummary("Create chain jobs");

        apiGroup
            .MapPut("/time-job/update", _UpdateTimeTicker<TTimeTicker, TCronTicker>)
            .WithName("UpdateTimeTicker")
            .WithSummary("Update time ticker");

        apiGroup
            .MapDelete("/time-job/delete", _DeleteTimeTicker<TTimeTicker, TCronTicker>)
            .WithName("DeleteTimeTicker")
            .WithSummary("Delete time ticker");

        apiGroup
            .MapDelete("/time-job/delete-batch", _DeleteTimeTickersBatch<TTimeTicker, TCronTicker>)
            .WithName("DeleteTimeTickersBatch")
            .WithSummary("Delete multiple time tickers");

        // Cron Jobs endpoints
        apiGroup
            .MapGet("/cron-jobs", _GetCronTickers<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickers")
            .WithSummary("Get all cron tickers");

        apiGroup
            .MapGet("/cron-jobs/paginated", _GetCronTickersPaginated<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersPaginated")
            .WithSummary("Get paginated cron tickers");

        apiGroup
            .MapGet("/cron-jobs/graph-data-range", _GetCronTickersGraphDataRange<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersGraphDataRange")
            .WithSummary("Get cron tickers graph data for specific date range");

        apiGroup
            .MapGet("/cron-jobs/graph-data-range-id", _GetCronTickersByIdGraphDataRange<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersByIdGraphDataRange")
            .WithSummary("Get cron ticker graph data by ID for specific date range");

        apiGroup
            .MapGet("/cron-jobs/graph-data", _GetCronTickersGraphData<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickersGraphData")
            .WithSummary("Get cron tickers graph data");

        apiGroup
            .MapGet("/cron-job-occurrences/{cronJobId}", _GetCronTickerOccurrences<TTimeTicker, TCronTicker>)
            .WithName("GetCronTickerOccurrences")
            .WithSummary("Get cron ticker occurrences");

        apiGroup
            .MapGet(
                "/cron-job-occurrences/{cronJobId}/paginated",
                _GetCronTickerOccurrencesPaginated<TTimeTicker, TCronTicker>
            )
            .WithName("GetCronTickerOccurrencesPaginated")
            .WithSummary("Get paginated cron ticker occurrences");

        apiGroup
            .MapGet(
                "/cron-job-occurrences/{cronJobId}/graph-data",
                _GetCronTickerOccurrencesGraphData<TTimeTicker, TCronTicker>
            )
            .WithName("GetCronTickerOccurrencesGraphData")
            .WithSummary("Get cron ticker occurrences graph data");

        apiGroup
            .MapPost("/cron-job/add", _AddCronTicker<TTimeTicker, TCronTicker>)
            .WithName("AddCronTicker")
            .WithSummary("Add cron ticker");

        apiGroup
            .MapPut("/cron-job/update", _UpdateCronTicker<TTimeTicker, TCronTicker>)
            .WithName("UpdateCronTicker")
            .WithSummary("Update cron ticker");

        apiGroup
            .MapPost("/cron-job/run", RunCronTickerOnDemand<TTimeTicker, TCronTicker>)
            .WithName("RunCronTickerOnDemand")
            .WithSummary("Run cron ticker on demand");

        apiGroup
            .MapDelete("/cron-job/delete", _DeleteCronTicker<TTimeTicker, TCronTicker>)
            .WithName("DeleteCronTicker")
            .WithSummary("Delete cron ticker");

        apiGroup
            .MapDelete("/cron-job-occurrence/delete", _DeleteCronTickerOccurrence<TTimeTicker, TCronTicker>)
            .WithName("DeleteCronTickerOccurrence")
            .WithSummary("Delete cron ticker occurrence");

        // Job operations
        apiGroup
            .MapPost("/job/cancel", _CancelTicker<TTimeTicker, TCronTicker>)
            .WithName("CancelTicker")
            .WithSummary("Cancel ticker by ID");

        apiGroup
            .MapGet("/job-request/{id}", _GetTickerRequest<TTimeTicker, TCronTicker>)
            .WithName("GetTickerRequest")
            .WithSummary("Get ticker request by ID");

        apiGroup
            .MapGet("/job-functions", _GetJobFunctions<TTimeTicker, TCronTicker>)
            .WithName("GetJobFunctions")
            .WithSummary("Get available job functions");

        // Host operations
        apiGroup
            .MapGet("/job-host/next-ticker", _GetNextTicker<TTimeTicker, TCronTicker>)
            .WithName("GetNextTicker")
            .WithSummary("Get next planned ticker");

        apiGroup
            .MapPost("/job-host/stop", _StopTickerHost<TTimeTicker, TCronTicker>)
            .WithName("StopTickerHost")
            .WithSummary("Stop ticker host");

        apiGroup
            .MapPost("/job-host/start", _StartTickerHost<TTimeTicker, TCronTicker>)
            .WithName("StartTickerHost")
            .WithSummary("Start ticker host");

        apiGroup
            .MapPost("/job-host/restart", _RestartTickerHost<TTimeTicker, TCronTicker>)
            .WithName("RestartTickerHost")
            .WithSummary("Restart ticker host");

        apiGroup
            .MapGet("/job-host/status", _GetTickerHostStatus<TTimeTicker, TCronTicker>)
            .WithName("GetTickerHostStatus")
            .WithSummary("Get ticker host status");

        // Statistics endpoints
        apiGroup
            .MapGet("/ticker/statuses/get-last-week", _GetLastWeekJobStatus<TTimeTicker, TCronTicker>)
            .WithName("GetLastWeekJobStatus")
            .WithSummary("Get last week job statuses");

        apiGroup
            .MapGet("/ticker/statuses/get", _GetJobStatuses<TTimeTicker, TCronTicker>)
            .WithName("GetJobStatuses")
            .WithSummary("Get overall job statuses");

        apiGroup
            .MapGet("/ticker/machine/jobs", _GetMachineJobs<TTimeTicker, TCronTicker>)
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

    private static IResult _GetOptions<TTimeTicker, TCronTicker>(
        JobsExecutionContext executionContext,
        SchedulerOptionsBuilder schedulerOptions,
        DashboardOptionsBuilder dashboardOptions
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
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

    private static async Task<IResult> _GetTimeTickers<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetTimeTickersAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetTimeTickersPaginated<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetTimeTickersPaginatedAsync(pageNumber, pageSize, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetTimeTickersGraphDataRange<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetTimeTickersGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetTimeTickersGraphData<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetTimeTickerFullDataAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _CreateChainJobs<TTimeTicker, TCronTicker>(
        HttpContext context,
        ITimeJobManager<TTimeTicker> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        string timeZoneId,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var chainRoot = JsonSerializer.Deserialize<TTimeTicker>(jsonString, dashboardOptions.DashboardJsonOptions);

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
                tickerId = result.Result?.Id,
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _UpdateTimeTicker<TTimeTicker, TCronTicker>(
        Guid id,
        HttpContext context,
        ITimeJobManager<TTimeTicker> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        string timeZoneId,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var timeTicker = JsonSerializer.Deserialize<TTimeTicker>(jsonString, dashboardOptions.DashboardJsonOptions)!;

        // Ensure the ID matches
        timeTicker.Id = id;

        if (timeTicker.ExecutionTime is { } executionTime && !string.IsNullOrEmpty(timeZoneId))
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var unspecified = DateTime.SpecifyKind(executionTime, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
            timeTicker.ExecutionTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        var result = await timeJobsManager.UpdateAsync(timeTicker, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time ticker updated successfully" : "Failed to update time ticker",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _DeleteTimeTicker<TTimeTicker, TCronTicker>(
        Guid id,
        ITimeJobManager<TTimeTicker> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await timeJobsManager.DeleteAsync(id, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time ticker deleted successfully" : "Failed to delete time ticker",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _DeleteTimeTickersBatch<TTimeTicker, TCronTicker>(
        [FromBody] Guid[] ids,
        ITimeJobManager<TTimeTicker> timeJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var idList = ids is { Length: > 0 } ? new List<Guid>(ids) : new List<Guid>();
        var result = await timeJobsManager.DeleteBatchAsync(idList, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Time tickers deleted successfully" : "Failed to delete time tickers",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetCronTickers<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickersPaginated<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersPaginatedAsync(pageNumber, pageSize, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickersGraphDataRange<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersGraphSpecificDataAsync(pastDays, futureDays, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickersByIdGraphDataRange<TTimeTicker, TCronTicker>(
        Guid id,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pastDays = 3,
        int futureDays = 3,
        CancellationToken cancellationToken = default
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersGraphSpecificDataByIdAsync(
            id,
            pastDays,
            futureDays,
            cancellationToken
        );
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickersGraphData<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickerFullDataAsync(cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickerOccurrences<TTimeTicker, TCronTicker>(
        Guid cronJobId,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersOccurrencesAsync(cronJobId, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickerOccurrencesPaginated<TTimeTicker, TCronTicker>(
        Guid cronJobId,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersOccurrencesPaginatedAsync(
            cronJobId,
            pageNumber,
            pageSize,
            cancellationToken
        );
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _GetCronTickerOccurrencesGraphData<TTimeTicker, TCronTicker>(
        Guid cronJobId,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await repository.GetCronTickersOccurrencesGraphDataAsync(cronJobId, cancellationToken);
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _AddCronTicker<TTimeTicker, TCronTicker>(
        HttpContext context,
        ICronJobManager<TCronTicker> cronJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var cronTicker = JsonSerializer.Deserialize<TCronTicker>(jsonString, dashboardOptions.DashboardJsonOptions)!;

        var result = await cronJobsManager.AddAsync(cronTicker, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron ticker added successfully" : "Failed to add cron ticker",
                tickerId = result.Result?.Id,
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _UpdateCronTicker<TTimeTicker, TCronTicker>(
        Guid id,
        HttpContext context,
        ICronJobManager<TCronTicker> cronJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        // Read the raw JSON from request body
        using var reader = new StreamReader(context.Request.Body);
        var jsonString = await reader.ReadToEndAsync(cancellationToken);

        // Use Dashboard-specific JSON options
        var cronTicker = JsonSerializer.Deserialize<TCronTicker>(jsonString, dashboardOptions.DashboardJsonOptions)!;

        // Ensure the ID matches
        cronTicker.Id = id;

        var result = await cronJobsManager.UpdateAsync(cronTicker, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron ticker updated successfully" : "Failed to update cron ticker",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> RunCronTickerOnDemand<TTimeTicker, TCronTicker>(
        Guid id,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        await repository.AddOnDemandCronTickerOccurrenceAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> _DeleteCronTicker<TTimeTicker, TCronTicker>(
        Guid id,
        ICronJobManager<TCronTicker> cronJobsManager,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = await cronJobsManager.DeleteAsync(id, cancellationToken);

        return Results.Json(
            new
            {
                success = result.IsSucceeded,
                message = result.IsSucceeded ? "Cron ticker deleted successfully" : "Failed to delete cron ticker",
            },
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _DeleteCronTickerOccurrence<TTimeTicker, TCronTicker>(
        Guid id,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        await repository.DeleteCronTickerOccurrenceByIdAsync(id, cancellationToken);
        return Results.Ok();
    }

    private static IResult _CancelTicker<TTimeTicker, TCronTicker>(
        Guid id,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        if (repository.CancelJobById(id))
        {
            return Results.Ok();
        }

        return Results.BadRequest();
    }

    private static async Task<IResult> _GetTickerRequest<TTimeTicker, TCronTicker>(
        Guid tickerId,
        JobType tickerType,
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var resultData = await repository.GetJobRequestByIdAsync(tickerId, tickerType, cancellationToken);

        var response = new { Result = resultData.Item1, MatchType = resultData.Item2 };
        return Results.Json(response, dashboardOptions.DashboardJsonOptions);
    }

    private static IResult _GetJobFunctions<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
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

    private static IResult _GetNextTicker<TTimeTicker, TCronTicker>(
        JobsExecutionContext executionContext,
        DashboardOptionsBuilder dashboardOptions
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var result = new { NextOccurrence = executionContext.GetNextPlannedOccurrence() };
        return Results.Json(result, dashboardOptions.DashboardJsonOptions);
    }

    private static async Task<IResult> _StopTickerHost<TTimeTicker, TCronTicker>(IJobsHostScheduler scheduler)
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        await scheduler.StopAsync();
        return Results.Ok();
    }

    private static async Task<IResult> _StartTickerHost<TTimeTicker, TCronTicker>(IJobsHostScheduler scheduler)
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        await scheduler.StartAsync();
        return Results.Ok();
    }

    private static IResult _RestartTickerHost<TTimeTicker, TCronTicker>(IJobsHostScheduler scheduler)
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        scheduler.Restart();
        return Results.Ok();
    }

    private static IResult _GetTickerHostStatus<TTimeTicker, TCronTicker>(IJobsHostScheduler scheduler)
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        return Results.Ok(new { scheduler.IsRunning });
    }

    private static async Task<IResult> _GetLastWeekJobStatus<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var jobStatuses = await repository.GetLastWeekJobStatusesAsync(cancellationToken);
        return Results.Json(
            jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetJobStatuses<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var jobStatuses = await repository.GetOverallJobStatusesAsync(cancellationToken);
        return Results.Json(
            jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    private static async Task<IResult> _GetMachineJobs<TTimeTicker, TCronTicker>(
        IJobsDashboardRepository<TTimeTicker, TCronTicker> repository,
        DashboardOptionsBuilder dashboardOptions,
        CancellationToken cancellationToken
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
        where TCronTicker : CronJobEntity, new()
    {
        var machineJobs = await repository.GetMachineJobsAsync(cancellationToken);
        return Results.Json(
            machineJobs.Select(x => new { item1 = x.Item1, item2 = x.Item2 }).ToArray(),
            dashboardOptions.DashboardJsonOptions
        );
    }

    #endregion
}
