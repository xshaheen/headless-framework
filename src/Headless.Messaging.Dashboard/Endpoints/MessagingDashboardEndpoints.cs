// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging.Dashboard;

public static class MessagingDashboardEndpoints
{
    internal const string PingHttpClientName = "Headless.Messaging.Dashboard.Ping";
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
            .RequireCors("HeadlessMessagingDashboardCORS")
            .AllowAnonymous();

        endpoints
            .MapPost("/api/auth/validate", _ValidateAuth)
            .WithName("Messaging_ValidateAuth")
            .WithSummary("Validate authentication credentials")
            .WithTags("Messaging Dashboard")
            .RequireCors("HeadlessMessagingDashboardCORS")
            .AllowAnonymous();

        // Always-anonymous endpoints
        endpoints
            .MapGet("/api/health", _Health)
            .WithName("Messaging_Health")
            .WithSummary("Health check endpoint")
            .WithTags("Messaging Dashboard")
            .RequireCors("HeadlessMessagingDashboardCORS")
            .AllowAnonymous();

        // Protected API group with gateway proxy filter
        var apiGroup = endpoints
            .MapGroup("/api")
            .WithTags("Messaging Dashboard")
            .RequireCors("HeadlessMessagingDashboardCORS")
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
        apiGroup
            .MapGet("/ping", _PingServices)
            .WithName("Messaging_PingServices")
            .WithSummary("Ping a registered node to measure latency");
    }

    #region Endpoint Handlers

    private static IResult _GetAuthInfo([FromServices] IAuthService authService)
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

    private static async Task<IResult> _ValidateAuth(HttpContext context, [FromServices] IAuthService authService)
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

    private static async Task<IResult> _Stats(IServiceProvider sp, HttpContext httpContext)
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
                var nodes = await discoveryProvider
                    .GetNodes(cancellationToken: httpContext.RequestAborted)
                    .ConfigureAwait(false);
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
                MessageId = message.Origin.Id,
                message.Origin.Name,
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
                MessageId = message.Origin.Id,
                message.Origin.Name,
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

            await dispatcher
                .EnqueueToExecute(message, descriptor: null, cancellationToken: httpContext.RequestAborted)
                .ConfigureAwait(false);
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

        if (!_TryParseStatusFilter(status, out var statusFilter))
        {
            // An unrecognized status matches no rows (the persisted status set is fixed).
            return Results.Json(
                _MapMessagePage(new IndexPage<MessageView>([], currentPage - 1, pageSize, totalItems: 0))
            );
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var queryDto = new MessageQuery
        {
            MessageType = MessageType.Publish,
            Name = name ?? string.Empty,
            Content = content ?? string.Empty,
            IntentType = intentType,
            StatusName = statusFilter,
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

        if (!_TryParseStatusFilter(status, out var statusFilter))
        {
            // An unrecognized status matches no rows (the persisted status set is fixed).
            return Results.Json(
                _MapMessagePage(new IndexPage<MessageView>([], currentPage - 1, pageSize, totalItems: 0))
            );
        }

        var dataStorage = sp.GetRequiredService<IDataStorage>();
        var monitoringApi = dataStorage.GetMonitoringApi();

        var queryDto = new MessageQuery
        {
            MessageType = MessageType.Subscribe,
            Group = group ?? string.Empty,
            Name = name ?? string.Empty,
            Content = content ?? string.Empty,
            IntentType = intentType,
            StatusName = statusFilter,
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
            // Serialize the status as its enum name (e.g. "Succeeded") so the dashboard SPA wire shape is
            // unchanged now that MessageView.StatusName is a StatusName enum rather than a string.
            StatusName = message.StatusName.ToString("G"),
            message.NextRetryAt,
            message.LockedUntil,
        };
    }

    private static bool _TryParseStatusFilter(string status, out StatusName statusFilter)
    {
        // Accept the enum member names case-insensitively (route segment). Reject numeric/undefined values so an
        // unknown status short-circuits to an empty page instead of falling through to an unfiltered query.
        return Enum.TryParse(status, ignoreCase: true, out statusFilter) && Enum.IsDefined(statusFilter);
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

    private static async Task<IResult> _Nodes(IServiceProvider sp, HttpContext httpContext)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<Node>());
        }

        var result =
            await discoveryProvider.GetNodes(cancellationToken: httpContext.RequestAborted).ConfigureAwait(false) ?? [];
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

    private static async Task<IResult> _ListServices(string @namespace, IServiceProvider sp, HttpContext httpContext)
    {
        var discoveryProvider = sp.GetService<INodeDiscoveryProvider>();
        if (discoveryProvider == null)
        {
            return Results.Json(new List<Node>());
        }

        var result = discoveryProvider is ICancellableNodeDiscoveryProvider cancellableDiscoveryProvider
            ? await cancellableDiscoveryProvider
                .ListServices(@namespace, httpContext.RequestAborted)
                .ConfigureAwait(false)
            : await discoveryProvider.ListServices(@namespace).ConfigureAwait(false);

        return Results.Json(result);
    }

    private static async Task<IResult> _PingServices(
        string? endpoint,
        IServiceProvider sp,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] MessagingDashboardOptionsBuilder config,
        HttpContext httpContext
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

        var nodes = await discoveryProvider
            .GetNodes(cancellationToken: httpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!_TryGetRegisteredOrigin(endpoint, nodes, out var registeredOrigin))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var httpClient = httpClientFactory.CreateClient(PingHttpClientName);
        var sw = new Stopwatch();
        try
        {
            sw.Restart();
            var healthEndpoint = _BuildHealthEndpoint(registeredOrigin, config.BasePath);
            var response = await httpClient
                .GetStringAsync(healthEndpoint, httpContext.RequestAborted)
                .ConfigureAwait(false);
            sw.Stop();

            if (string.Equals(response, "OK", StringComparison.Ordinal))
            {
                return Results.Text(sw.ElapsedMilliseconds.ToString("D", CultureInfo.InvariantCulture));
            }

            return Results.StatusCode(501);
        }
        catch (HttpRequestException e)
            when (e.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException e)
        {
            return Results.StatusCode((int)(e.StatusCode ?? HttpStatusCode.BadGateway));
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable ERP022 // Dashboard health probe should return gateway failure for unexpected probe exceptions.
        catch
        {
            return Results.StatusCode((int)HttpStatusCode.BadGateway);
        }
#pragma warning restore ERP022
    }

    private static bool _TryGetRegisteredOrigin(
        string endpoint,
        IEnumerable<Node> nodes,
        [NotNullWhen(true)] out Uri? origin
    )
    {
        origin = null;
        if (!_TryCreateOrigin(endpoint, out var requestedOrigin))
        {
            return false;
        }

        foreach (var node in nodes)
        {
            if (!_TryCreateNodeOrigin(node, out var nodeOrigin) || !_HasSameOrigin(requestedOrigin, nodeOrigin))
            {
                continue;
            }

            origin = requestedOrigin;
            return true;
        }

        return false;
    }

    private static bool _TryCreateOrigin(string value, [NotNullWhen(true)] out Uri? origin)
    {
        origin = null;
        var valueSpan = value.AsSpan();
        if (valueSpan.Contains('?') || valueSpan.Contains('#'))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (
            !parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (
            string.IsNullOrEmpty(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.Equals(parsed.AbsolutePath, "/", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
        )
        {
            return false;
        }

        origin = parsed;
        return true;
    }

    private static bool _TryCreateNodeOrigin(Node node, [NotNullWhen(true)] out Uri? origin)
    {
        var address = node.Address.Contains(Uri.SchemeDelimiter, StringComparison.Ordinal)
            ? node.Address
            : Uri.UriSchemeHttp + Uri.SchemeDelimiter + node.Address;

        if (!_TryCreateOrigin(address, out var parsed))
        {
            origin = null;
            return false;
        }

        if (node.Port <= 0)
        {
            origin = parsed;
            return true;
        }

        origin = new UriBuilder(parsed) { Port = node.Port }.Uri;
        return true;
    }

    private static bool _HasSameOrigin(Uri left, Uri right)
    {
        return left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
            && left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port;
    }

    private static Uri _BuildHealthEndpoint(Uri origin, string basePath)
    {
        var normalizedBasePath = DashboardSpaHelper.NormalizeBasePath(basePath);
        var healthPath = string.Equals(normalizedBasePath, "/", StringComparison.Ordinal)
            ? "/api/health"
            : normalizedBasePath + "/api/health";

        return new UriBuilder(origin)
        {
            Path = healthPath,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;
    }

    #endregion
}

internal sealed class WarpResult
{
    public int ChildCount => Values.Count;

    public required string Group { get; set; }

    public required List<SubInfo> Values { get; set; }

    internal sealed class SubInfo
    {
        public required string MessageName { get; set; }

        public required string ImplName { get; set; }

        public required string MethodEscaped { get; set; }
    }
}
