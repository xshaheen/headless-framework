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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Headless.Messaging.Dashboard;

public static class MessagingDashboardEndpoints
{
    private const int _MaxPageSize = 200;

    internal static void MapMessagingDashboardEndpoints(
        this IEndpointRouteBuilder endpoints,
        MessagingDashboardOptionsBuilder config
    )
    {
        // Auth endpoints (always anonymous)
        endpoints
            .MapGet("/api/auth/info", _GetAuthInfo)
            .WithName("Messaging_GetAuthInfo")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        endpoints
            .MapPost("/api/auth/validate", _ValidateAuth)
            .WithName("Messaging_ValidateAuth")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        // Always-anonymous endpoints
        endpoints
            .MapGet("/api/health", _Health)
            .WithName("Messaging_Health")
            .WithTags("Messaging Dashboard")
            .RequireCors("Messaging_Dashboard_CORS")
            .AllowAnonymous();

        endpoints
            .MapGet("/api/ping", _PingServices)
            .WithName("Messaging_PingServices")
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
        apiGroup.MapGet("/metrics-realtime", _Metrics).WithName("Messaging_Metrics");
        apiGroup.MapGet("/meta", _MetaInfo).WithName("Messaging_MetaInfo");
        apiGroup.MapGet("/stats", _Stats).WithName("Messaging_Stats");
        apiGroup.MapGet("/metrics-history", _MetricsHistory).WithName("Messaging_MetricsHistory");

        // Published messages
        apiGroup.MapGet("/published/message/{id:long}", _PublishedMessageDetails).WithName("Messaging_PublishedMessageDetails");
        apiGroup.MapPost("/published/requeue", _PublishedRequeue).WithName("Messaging_PublishedRequeue");
        apiGroup.MapPost("/published/delete", _PublishedDelete).WithName("Messaging_PublishedDelete");
        apiGroup.MapGet("/published/{status}", _PublishedList).WithName("Messaging_PublishedList");

        // Received messages
        apiGroup.MapGet("/received/message/{id:long}", _ReceivedMessageDetails).WithName("Messaging_ReceivedMessageDetails");
        apiGroup.MapPost("/received/reexecute", _ReceivedRequeue).WithName("Messaging_ReceivedRequeue");
        apiGroup.MapPost("/received/delete", _ReceivedDelete).WithName("Messaging_ReceivedDelete");
        apiGroup.MapGet("/received/{status}", _ReceivedList).WithName("Messaging_ReceivedList");

        // Subscribers
        apiGroup.MapGet("/subscriber", _Subscribers).WithName("Messaging_Subscribers");

        // Nodes & discovery
        apiGroup.MapGet("/nodes", _Nodes).WithName("Messaging_Nodes");
        apiGroup.MapGet("/list-ns", _ListNamespaces).WithName("Messaging_ListNamespaces");
        apiGroup.MapGet("/list-svc/{namespace}", _ListServices).WithName("Messaging_ListServices");
    }

    #region Endpoint Handlers

    private static IResult _GetAuthInfo(IAuthService authService)
    {
        var authInfo = authService.GetAuthInfo();
        return Results.Json(new
        {
            mode = authInfo.Mode.ToString().ToLower(CultureInfo.InvariantCulture),
            enabled = authInfo.IsEnabled,
            sessionTimeout = authInfo.SessionTimeoutMinutes,
        });
    }

    private static async Task<IResult> _ValidateAuth(HttpContext context, IAuthService authService)
    {
        var authResult = await authService.AuthenticateAsync(context);

        if (authResult.IsAuthenticated)
        {
            return Results.Json(new
            {
                authenticated = true,
                username = authResult.Username,
                message = "Authentication successful",
            });
        }

        return Results.Unauthorized();
    }

    private static IResult _Health()
    {
        return Results.Text("OK");
    }

    private static async Task<IResult> _Metrics(IServiceProvider sp)
    {
        var metrics = sp.GetRequiredService<MessagingMetricsEventListener>();
        return Results.Json(metrics.GetRealTimeMetrics());
    }

    private static IResult _MetaInfo(IServiceProvider sp)
    {
        var messaging = sp.GetService<MessagingMarkerService>();
        var broker = sp.GetService<MessageQueueMarkerService>();
        var storage = sp.GetService<MessageStorageMarkerService>();

        return Results.Json(new { messaging, broker, storage });
    }

    private static async Task<IResult> _Stats(IServiceProvider sp)
    {
        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var result = await monitoringApi.GetStatisticsAsync();

        // Try to set server count from cache or discovery
        if (MessagingCache.Global.TryGet("messaging.nodes.count", out var count))
        {
            result.Servers = (int)count;
        }
        else
        {
            if (sp.GetService<ConsulDiscoveryOptions>() != null)
            {
                var discoveryProvider = sp.GetRequiredService<INodeDiscoveryProvider>();
                var nodes = await discoveryProvider.GetNodes();
                result.Servers = nodes.Count;
            }
        }

        return Results.Json(result);
    }

    private static async Task<IResult> _MetricsHistory(IServiceProvider sp)
    {
        const string cacheKey = "dashboard.metrics.history";
        if (MessagingCache.Global.TryGet(cacheKey, out var ret))
        {
            return Results.Json(ret);
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var ps = await monitoringApi.HourlySucceededJobs(MessageType.Publish);
        var pf = await monitoringApi.HourlyFailedJobs(MessageType.Publish);
        var ss = await monitoringApi.HourlySucceededJobs(MessageType.Subscribe);
        var sf = await monitoringApi.HourlyFailedJobs(MessageType.Subscribe);

        var dayHour = ps.Keys.OrderBy(x => x).Select(x => new DateTimeOffset(x).ToUnixTimeSeconds());

        var result = new
        {
            DayHour = dayHour.ToArray(),
            PublishSuccessed = ps.Values.Reverse(),
            PublishFailed = pf.Values.Reverse(),
            SubscribeSuccessed = ss.Values.Reverse(),
            SubscribeFailed = sf.Values.Reverse(),
        };

        MessagingCache.Global.AddOrUpdate(cacheKey, result, TimeSpan.FromMinutes(10));

        return Results.Json(result);
    }

    private static async Task<IResult> _PublishedMessageDetails(long id, IServiceProvider sp)
    {
        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var message = await monitoringApi.GetPublishedMessageAsync(id);
        if (message == null)
        {
            return Results.NotFound();
        }

        return Results.Json(message.Content);
    }

    private static async Task<IResult> _ReceivedMessageDetails(long id, IServiceProvider sp)
    {
        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var message = await monitoringApi.GetReceivedMessageAsync(id);
        if (message == null)
        {
            return Results.NotFound();
        }

        return Results.Json(message.Content);
    }

    private static async Task<IResult> _PublishedRequeue(HttpContext httpContext, IServiceProvider sp)
    {
        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();
        var dispatcher = sp.GetRequiredService<IDispatcher>();

        foreach (var messageId in messageIds)
        {
            var message = await monitoringApi.GetPublishedMessageAsync(messageId);
            if (message != null)
            {
                await dispatcher.EnqueueToPublish(message, httpContext.RequestAborted);
            }
        }

        return Results.NoContent();
    }

    private static async Task<IResult> _PublishedDelete(HttpContext httpContext, IServiceProvider sp)
    {
        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();

        foreach (var messageId in messageIds)
        {
            _ = await dataStorage.DeletePublishedMessageAsync(messageId);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> _ReceivedRequeue(HttpContext httpContext, IServiceProvider sp)
    {
        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();
        var dispatcher = sp.GetRequiredService<IDispatcher>();

        foreach (var messageId in messageIds)
        {
            var message = await monitoringApi.GetReceivedMessageAsync(messageId);
            if (message != null)
            {
                await dispatcher.EnqueueToExecute(message, null, httpContext.RequestAborted);
            }
        }

        return Results.NoContent();
    }

    private static async Task<IResult> _ReceivedDelete(HttpContext httpContext, IServiceProvider sp)
    {
        var messageIds = await httpContext.Request.ReadFromJsonAsync<long[]>();
        if (messageIds == null || messageIds.Length == 0)
        {
            return Results.UnprocessableEntity();
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();

        foreach (var messageId in messageIds)
        {
            _ = await dataStorage.DeleteReceivedMessageAsync(messageId);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> _PublishedList(
        string status,
        IServiceProvider sp,
        string? name = null,
        string? content = null,
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
            StatusName = status,
            CurrentPage = currentPage - 1,
            PageSize = pageSize,
        };

        var result = await monitoringApi.GetMessagesAsync(queryDto);
        return Results.Json(result);
    }

    private static async Task<IResult> _ReceivedList(
        string status,
        IServiceProvider sp,
        string? name = null,
        string? group = null,
        string? content = null,
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
            StatusName = status,
            CurrentPage = currentPage - 1,
            PageSize = pageSize,
        };

        var result = await monitoringApi.GetMessagesAsync(queryDto);
        return Results.Json(result);
    }

    private static async Task<IResult> _Subscribers(IServiceProvider sp)
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
                        Topic = descriptor.TopicName,
                        ImplName = descriptor.ImplTypeInfo.Name,
                        MethodEscaped = HtmlHelper.MethodEscaped(descriptor.MethodInfo),
                    }
                );
            }

            result.Add(inner);
        }

        return Results.Json(result);
    }

    private static async Task<IResult> _Nodes(IServiceProvider sp)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<Node>());
        }

        var result = await discoveryProvider.GetNodes() ?? [];
        return Results.Json(result);
    }

    private static async Task<IResult> _ListNamespaces(HttpContext httpContext, IServiceProvider sp)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<string>());
        }

        var nsList = await discoveryProvider.GetNamespaces(httpContext.RequestAborted);
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

        var result = await discoveryProvider.ListServices(@namespace);
        return Results.Json(result);
    }

    private static async Task<IResult> _PingServices(
        string? endpoint,
        IServiceProvider sp,
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

        var nodes = await discoveryProvider.GetNodes();
        var isRegistered = nodes.Any(n =>
            endpoint.StartsWith($"http://{n.Address}:{n.Port}", StringComparison.OrdinalIgnoreCase)
            || endpoint.StartsWith($"https://{n.Address}:{n.Port}", StringComparison.OrdinalIgnoreCase)
        );

        if (!isRegistered)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var httpClient = new HttpClient();
        var sw = new Stopwatch();
        try
        {
            sw.Restart();
            var healthEndpoint = endpoint + config.BasePath + "/api/health";
            var response = await httpClient.GetStringAsync(healthEndpoint);
            sw.Stop();

            if (response == "OK")
            {
                return Results.Text(sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            }

            return Results.StatusCode(501);
        }
        catch (HttpRequestException e)
        {
            return Results.StatusCode((int)(e.StatusCode ?? HttpStatusCode.BadGateway));
        }
#pragma warning disable EPC12
        catch
        {
            return Results.StatusCode((int)HttpStatusCode.BadGateway);
        }
#pragma warning restore EPC12
    }

    #endregion
}

public class WarpResult
{
    public int ChildCount => Values.Count;

    public required string Group { get; set; }

    public required List<SubInfo> Values { get; set; }

    public class SubInfo
    {
        public required string Topic { get; set; }

        public required string ImplName { get; set; }

        public required string MethodEscaped { get; set; }
    }
}

/// <summary>
/// Extension to parse StringValues to int with a default.
/// </summary>
public static class IntExtension
{
    public static int ToInt32OrDefault(this StringValues value, int defaultValue = 0)
    {
        return int.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;
    }
}
