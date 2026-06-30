// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging.Dashboard;

public static class MessagingDashboardEndpoints
{
    private const int _MaxPageSize = 200;
    private const int _MaxBulkActionSize = 500;

    internal static void MapMessagingDashboardEndpoints(
        this IEndpointRouteBuilder endpoints,
        MessagingDashboardOptionsBuilder config
    )
    {
        // Auth endpoints (always anonymous)
        endpoints
            .MapGet("/api/auth/info", _GetAuthInfo)
            .WithName("Messaging_GetAuthInfo")
            .WithSummary("Get authentication configuration")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        endpoints
            .MapPost("/api/auth/validate", _ValidateAuth)
            .WithName("Messaging_ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        // Always-anonymous endpoints
        endpoints
            .MapGet("/api/health", _Health)
            .WithName("Messaging_Health")
            .WithSummary("Health check endpoint")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        endpoints
            .MapGet("/api/ping", _PingServices)
            .WithName("Messaging_PingServices")
            .WithSummary("Ping a registered node to measure latency")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        // Protected API group with gateway proxy filter
        var apiGroup = endpoints
            .MapGroup("/api")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AddEndpointFilter<GatewayProxyEndpointFilter>();

        // Apply host auth if configured
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

        // Metrics & stats
        apiGroup
            .MapGet("/metrics-realtime", _Metrics)
            .WithName("Messaging_Metrics")
            .WithSummary("Get real-time messaging metrics");
        apiGroup
            .MapGet("/meta", _MetaInfo)
            .WithName("Messaging_MetaInfo")
            .WithSummary("Get messaging infrastructure metadata");
        apiGroup.MapGet("/stats", _Stats).WithName("Messaging_Stats").WithSummary("Get aggregate message statistics");
        apiGroup
            .MapGet("/metrics-history", _MetricsHistory)
            .WithName("Messaging_MetricsHistory")
            .WithSummary("Get hourly metrics history for the last 24 hours");

        // Published messages
        apiGroup
            .MapGet("/published/message/{id:guid}", _PublishedMessageDetails)
            .WithName("Messaging_PublishedMessageDetails")
            .WithSummary("Get published message details by ID");
        apiGroup
            .MapPost("/published/requeue", _PublishedRequeue)
            .WithName("Messaging_PublishedRequeue")
            .WithSummary("Requeue published messages for redelivery")
            .WithDescription("Accepts a JSON array of message IDs (Guid[]) in the request body.");
        apiGroup
            .MapPost("/published/delete", _PublishedDelete)
            .WithName("Messaging_PublishedDelete")
            .WithSummary("Delete published messages")
            .WithDescription("Accepts a JSON array of message IDs (Guid[]) in the request body.");
        apiGroup
            .MapGet("/published/{status}", _PublishedList)
            .WithName("Messaging_PublishedList")
            .WithSummary("List published messages by status")
            .WithDescription("Valid status values: Succeeded, Failed, Delayed, Scheduled, Queued.");

        // Received messages
        apiGroup
            .MapGet("/received/message/{id:guid}", _ReceivedMessageDetails)
            .WithName("Messaging_ReceivedMessageDetails")
            .WithSummary("Get received message details by ID");
        apiGroup
            .MapPost("/received/reexecute", _ReceivedRequeue)
            .WithName("Messaging_ReceivedRequeue")
            .WithSummary("Re-execute received messages")
            .WithDescription("Accepts a JSON array of message IDs (Guid[]) in the request body.");
        apiGroup
            .MapPost("/received/delete", _ReceivedDelete)
            .WithName("Messaging_ReceivedDelete")
            .WithSummary("Delete received messages")
            .WithDescription("Accepts a JSON array of message IDs (Guid[]) in the request body.");
        apiGroup
            .MapGet("/received/{status}", _ReceivedList)
            .WithName("Messaging_ReceivedList")
            .WithSummary("List received messages by status")
            .WithDescription("Valid status values: Succeeded, Failed, Delayed, Scheduled, Queued.");

        // Subscribers
        apiGroup
            .MapGet("/subscriber", _Subscribers)
            .WithName("Messaging_Subscribers")
            .WithSummary("Get all registered message subscribers");

        // Nodes & discovery
        apiGroup.MapGet("/nodes", _Nodes).WithName("Messaging_Nodes").WithSummary("Get registered messaging nodes");
        apiGroup
            .MapGet("/list-ns", _ListNamespaces)
            .WithName("Messaging_ListNamespaces")
            .WithSummary("List available namespaces for node discovery");
        apiGroup
            .MapGet("/list-svc/{namespace}", _ListServices)
            .WithName("Messaging_ListServices")
            .WithSummary("List services in a namespace");
    }

    #region Endpoint Handlers

    private static IResult _GetAuthInfo(IAuthService authService)
    {
        var authInfo = authService.GetAuthInfo();
        return Results.Json(
            new
            {
                mode = authInfo.Mode.ToString().ToLower(CultureInfo.InvariantCulture),
                enabled = authInfo.IsEnabled,
                sessionTimeout = authInfo.SessionTimeoutMinutes,
            }
        );
    }

    private static async Task<IResult> _ValidateAuth(HttpContext context, IAuthService authService)
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
                }
            );
        }

        return Results.Unauthorized();
    }

    private static IResult _Health()
    {
        return Results.Text("OK");
    }

    private static Task<IResult> _Metrics(IServiceProvider sp)
    {
        try
        {
            var metrics = sp.GetRequiredService<MessagingMetricsEventListener>();
            return Task.FromResult(Results.Json(metrics.GetRealTimeMetrics()));
        }
        catch (Exception exception)
        {
            return Task.FromException<IResult>(exception);
        }
    }

    private static IResult _MetaInfo(IServiceProvider sp)
    {
        var messaging = sp.GetService<MessagingMarkerService>();
        var broker = sp.GetService<MessageQueueMarkerService>();
        var storage = sp.GetService<MessageStorageMarkerService>();

        return Results.Json(
            new
            {
                messaging,
                broker,
                storage,
            }
        );
    }

    private static async Task<IResult> _Stats(IServiceProvider sp)
    {
        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var result = await monitoringApi.GetStatisticsAsync().ConfigureAwait(false);

        // Set subscriber count
        var subscriberCache = sp.GetRequiredService<MethodMatcherCache>();
        result.Subscribers = subscriberCache.GetCandidatesMethodsOfGroupNameGrouped().Sum(g => g.Value.Count);

        // Try to set server count from cache or discovery
        var cache = sp.GetRequiredService<IMemoryCache>();
        if (cache.TryGetValue("messaging.nodes.count", out int count))
        {
            result.Servers = count;
        }
        else
        {
            if (sp.GetService<ConsulDiscoveryOptions>() != null)
            {
                var discoveryProvider = sp.GetRequiredService<INodeDiscoveryProvider>();
                var nodes = await discoveryProvider.GetNodes().ConfigureAwait(false);
                result.Servers = nodes.Count;
            }
        }

        return Results.Json(result);
    }

    private static async Task<IResult> _MetricsHistory(IServiceProvider sp)
    {
        const string cacheKey = "dashboard.metrics.history";
        var cache = sp.GetRequiredService<IMemoryCache>();
        if (cache.TryGetValue(cacheKey, out var ret))
        {
            return Results.Json(ret);
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var ps = await monitoringApi.HourlySucceededJobs(MessageType.Publish).ConfigureAwait(false);
        var pf = await monitoringApi.HourlyFailedJobs(MessageType.Publish).ConfigureAwait(false);
        var ss = await monitoringApi.HourlySucceededJobs(MessageType.Subscribe).ConfigureAwait(false);
        var sf = await monitoringApi.HourlyFailedJobs(MessageType.Subscribe).ConfigureAwait(false);

        var dayHour = ps.Keys.Order().Select(x => new DateTimeOffset(x).ToUnixTimeSeconds());

        var result = new
        {
            DayHour = dayHour.ToArray(),
            PublishSuccessed = ps.Values.Reverse(),
            PublishFailed = pf.Values.Reverse(),
            SubscribeSuccessed = ss.Values.Reverse(),
            SubscribeFailed = sf.Values.Reverse(),
        };

        cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));

        return Results.Json(result);
    }

    private static async Task<IResult> _PublishedMessageDetails(Guid id, IServiceProvider sp)
    {
        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var message = await monitoringApi.GetPublishedMessageAsync(id).ConfigureAwait(false);
        if (message == null)
        {
            return Results.NotFound();
        }

        return Results.Json(
            new
            {
                StorageId = message.StorageId.ToString("D"),
                MessageId = message.Origin.GetId(),
                Name = message.Origin.GetName(),
                message.IntentType,
                message.Content,
                message.Added,
                message.ExpiresAt,
                message.Retries,
            }
        );
    }

    private static async Task<IResult> _ReceivedMessageDetails(Guid id, IServiceProvider sp)
    {
        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var message = await monitoringApi.GetReceivedMessageAsync(id).ConfigureAwait(false);
        if (message == null)
        {
            return Results.NotFound();
        }

        return Results.Json(
            new
            {
                StorageId = message.StorageId.ToString("D"),
                MessageId = message.Origin.GetId(),
                Name = message.Origin.GetName(),
                Group = message.Origin.GetGroup(),
                message.IntentType,
                message.Content,
                message.Added,
                message.ExpiresAt,
                message.Retries,
                message.ExceptionInfo,
            }
        );
    }

    private static async Task<IResult> _PublishedRequeue(HttpContext httpContext, IServiceProvider sp)
    {
        var storageIds = await _ReadStorageIdsAsync(httpContext).ConfigureAwait(false);
        if (storageIds == null || storageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        if (storageIds.Length > _MaxBulkActionSize)
        {
            return _BulkTooLarge();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();
        var dispatcher = sp.GetRequiredService<IDispatcher>();
        var busTransport = sp.GetService<IBusTransport>();
        var queueTransport = sp.GetService<IQueueTransport>();

        var messages = await monitoringApi
            .GetPublishedMessagesAsync(storageIds, httpContext.RequestAborted)
            .ConfigureAwait(false);

        var rejected = new List<Guid>();
        var requeued = new List<Guid>();

        foreach (var message in messages)
        {
            var hasTransport = message.IntentType switch
            {
                IntentType.Bus => busTransport is not null,
                IntentType.Queue => queueTransport is not null,
                _ => false,
            };

            if (!hasTransport)
            {
                rejected.Add(message.StorageId);
                continue;
            }

            await dispatcher.EnqueueToPublish(message, httpContext.RequestAborted).ConfigureAwait(false);
            requeued.Add(message.StorageId);
        }

        if (rejected.Count > 0)
        {
            return Results.UnprocessableEntity(new { rejected, requeued });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> _PublishedDelete(HttpContext httpContext, IServiceProvider sp)
    {
        var storageIds = await _ReadStorageIdsAsync(httpContext).ConfigureAwait(false);
        if (storageIds == null || storageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        if (storageIds.Length > _MaxBulkActionSize)
        {
            return _BulkTooLarge();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        _ = await dataStorage
            .DeletePublishedMessagesAsync(storageIds, httpContext.RequestAborted)
            .ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> _ReceivedRequeue(HttpContext httpContext, IServiceProvider sp)
    {
        var storageIds = await _ReadStorageIdsAsync(httpContext).ConfigureAwait(false);
        if (storageIds == null || storageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        if (storageIds.Length > _MaxBulkActionSize)
        {
            return _BulkTooLarge();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();
        var dispatcher = sp.GetRequiredService<IDispatcher>();
        var busTransport = sp.GetService<IBusTransport>();
        var queueTransport = sp.GetService<IQueueTransport>();

        var messages = await monitoringApi
            .GetReceivedMessagesAsync(storageIds, httpContext.RequestAborted)
            .ConfigureAwait(false);

        var rejected = new List<Guid>();
        var requeued = new List<Guid>();

        foreach (var message in messages)
        {
            var hasTransport = message.IntentType switch
            {
                IntentType.Bus => busTransport is not null,
                IntentType.Queue => queueTransport is not null,
                _ => false,
            };

            if (!hasTransport)
            {
                rejected.Add(message.StorageId);
                continue;
            }

            await dispatcher.EnqueueToExecute(message, null, httpContext.RequestAborted).ConfigureAwait(false);
            requeued.Add(message.StorageId);
        }

        if (rejected.Count > 0)
        {
            return Results.UnprocessableEntity(new { rejected, requeued });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> _ReceivedDelete(HttpContext httpContext, IServiceProvider sp)
    {
        var storageIds = await _ReadStorageIdsAsync(httpContext).ConfigureAwait(false);
        if (storageIds == null || storageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        if (storageIds.Length > _MaxBulkActionSize)
        {
            return _BulkTooLarge();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        _ = await dataStorage.DeleteReceivedMessagesAsync(storageIds, httpContext.RequestAborted).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> _PublishedList(
        string status,
        IServiceProvider sp,
        string? name = null,
        string? content = null,
        IntentType? intentType = null,
        int perPage = 20,
        int currentPage = 1
    )
    {
        var pageSize = Math.Clamp(perPage, 1, _MaxPageSize);

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var queryDto = new MessageQuery
        {
            MessageType = MessageType.Publish,
            Name = name ?? string.Empty,
            Content = content ?? string.Empty,
            IntentType = intentType,
            StatusName = status,
            CurrentPage = currentPage - 1,
            PageSize = pageSize,
        };

        var result = await monitoringApi.GetMessagesAsync(queryDto).ConfigureAwait(false);
        return Results.Json(_MapMessagePage(result));
    }

    private static async Task<IResult> _ReceivedList(
        string status,
        IServiceProvider sp,
        string? name = null,
        string? group = null,
        string? content = null,
        IntentType? intentType = null,
        int perPage = 20,
        int currentPage = 1
    )
    {
        var pageSize = Math.Clamp(perPage, 1, _MaxPageSize);

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var queryDto = new MessageQuery
        {
            MessageType = MessageType.Subscribe,
            Group = group ?? string.Empty,
            Name = name ?? string.Empty,
            Content = content ?? string.Empty,
            IntentType = intentType,
            StatusName = status,
            CurrentPage = currentPage - 1,
            PageSize = pageSize,
        };

        var result = await monitoringApi.GetMessagesAsync(queryDto).ConfigureAwait(false);
        return Results.Json(_MapMessagePage(result));
    }

    private static Task<IResult> _Subscribers(IServiceProvider sp)
    {
        try
        {
            var cache = sp.GetRequiredService<MethodMatcherCache>();
            var subscribers = cache.GetCandidatesMethodsOfGroupNameGrouped();

            var result = new List<WarpResult>();

            foreach (var subscriber in subscribers)
            {
                var inner = new WarpResult { Group = subscriber.Key, Values = [] };
                foreach (var descriptor in subscriber.Value)
                {
                    inner.Values.Add(
                        new WarpResult.SubInfo
                        {
                            MessageName = descriptor.MessageName,
                            ImplName = descriptor.ImplTypeInfo.Name,
                            MethodEscaped = HtmlHelper.MethodEscaped(descriptor.MethodInfo),
                        }
                    );
                }

                result.Add(inner);
            }

            return Task.FromResult(Results.Json(result));
        }
        catch (Exception exception)
        {
            return Task.FromException<IResult>(exception);
        }
    }

    private static object _MapMessageView(MessageView message)
    {
        return new
        {
            StorageId = message.StorageId.ToString("D"),
            message.MessageId,
            message.Group,
            message.Name,
            message.IntentType,
            message.Content,
            message.Added,
            message.ExpiresAt,
            message.Retries,
            message.StatusName,
            message.NextRetryAt,
            message.LockedUntil,
        };
    }

    private static object _MapMessagePage(IndexPage<MessageView> page)
    {
        var mapped = page.Select(_MapMessageView);
        return new
        {
            mapped.Items,
            mapped.Index,
            mapped.Size,
            mapped.TotalItems,
            mapped.TotalPages,
            mapped.HasPrevious,
            mapped.HasNext,
            Totals = mapped.TotalItems,
        };
    }

    private static IResult _BulkTooLarge()
    {
        return Results.UnprocessableEntity(
            new
            {
                error = $"Bulk action exceeds the maximum of {_MaxBulkActionSize} ids.",
                maxBulkSize = _MaxBulkActionSize,
            }
        );
    }

    private static async ValueTask<Guid[]?> _ReadStorageIdsAsync(HttpContext httpContext)
    {
        try
        {
            var payload = await httpContext
                .Request.ReadFromJsonAsync<JsonElement>(httpContext.RequestAborted)
                .ConfigureAwait(false);
            if (payload.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var storageIds = new List<Guid>();
            foreach (var item in payload.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var stringStorageId))
                {
                    storageIds.Add(stringStorageId);
                    continue;
                }

                return null;
            }

            return [.. storageIds];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<IResult> _Nodes(IServiceProvider sp)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<Node>());
        }

        var result = await discoveryProvider.GetNodes().ConfigureAwait(false) ?? [];
        return Results.Json(result);
    }

    private static async Task<IResult> _ListNamespaces(HttpContext httpContext, IServiceProvider sp)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<string>());
        }

        var nsList = await discoveryProvider.GetNamespaces(httpContext.RequestAborted).ConfigureAwait(false);
        if (nsList == null)
        {
            return Results.NotFound();
        }

        return Results.Json(nsList);
    }

    private static async Task<IResult> _ListServices(string @namespace, IServiceProvider sp)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<Node>());
        }

        var result = await discoveryProvider.ListServices(@namespace).ConfigureAwait(false);
        return Results.Json(result);
    }

    private static async Task<IResult> _PingServices(
        string? endpoint,
        IServiceProvider sp,
        IHttpClientFactory httpClientFactory,
        MessagingDashboardOptionsBuilder config
    )
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Results.BadRequest("Missing endpoint parameter.");
        }

        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.BadRequest("Node discovery is not configured.");
        }

        var nodes = await discoveryProvider.GetNodes().ConfigureAwait(false);
        var isRegistered = nodes.Any(n =>
            endpoint.StartsWith(
                string.Create(CultureInfo.InvariantCulture, $"http://{n.Address}:{n.Port}"),
                StringComparison.OrdinalIgnoreCase
            )
            || endpoint.StartsWith(
                string.Create(CultureInfo.InvariantCulture, $"https://{n.Address}:{n.Port}"),
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (!isRegistered)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var httpClient = httpClientFactory.CreateClient();
        var sw = new Stopwatch();
        try
        {
            sw.Restart();
            var healthEndpoint = endpoint + config.BasePath + "/api/health";
            var response = await httpClient.GetStringAsync(healthEndpoint).ConfigureAwait(false);
            sw.Stop();

            if (string.Equals(response, "OK", StringComparison.Ordinal))
            {
                return Results.Text(sw.ElapsedMilliseconds.ToString("D", CultureInfo.InvariantCulture));
            }

            return Results.StatusCode(501);
        }
        catch (HttpRequestException e)
        {
            return Results.StatusCode((int)(e.StatusCode ?? HttpStatusCode.BadGateway));
        }
#pragma warning disable ERP022 // Dashboard health probe should return gateway failure for unexpected probe exceptions.
        catch
        {
            return Results.StatusCode((int)HttpStatusCode.BadGateway);
        }
#pragma warning restore ERP022
    }

    #endregion
}

internal sealed class WarpResult
{
    public int ChildCount => Values.Count;

    public required string Group { get; set; }

    public required List<SubInfo> Values { get; set; }

    public sealed class SubInfo
    {
        public required string MessageName { get; set; }

        public required string ImplName { get; set; }

        public required string MethodEscaped { get; set; }
    }
}
