// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Api.Authorization;

/// <summary>
/// Records an audit entry for every authorization challenge and forbid, then hands the result to the wrapped
/// handler so the response is exactly what it would have been without auditing.
/// </summary>
internal sealed partial class AuditingAuthorizationMiddlewareResultHandler<TContext>(
    IAuthorizationMiddlewareResultHandler inner,
    ILogger<AuditingAuthorizationMiddlewareResultHandler<TContext>> logger
) : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult
    )
    {
        if (authorizeResult.Challenged || authorizeResult.Forbidden)
        {
            await _AuditAsync(context, authorizeResult.Forbidden).ConfigureAwait(false);
        }

        await inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }

    private async Task _AuditAsync(HttpContext context, bool forbidden)
    {
        var endpoint = context.GetEndpoint();
        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["method"] = context.Request.Method,
            // The route template, never the request path: the path carries the resource IDs and query values the
            // caller sent, and an audit row must not become a copy of request input.
            ["route"] = (endpoint as RouteEndpoint)?.RoutePattern.RawText,
            ["policies"] = _GetPolicyNames(endpoint),
        };

        var request = new AuditLogWriteRequest
        {
            Action = forbidden ? "authorization.forbidden" : "authorization.challenged",
            Data = data,
            Success = false,
        };

        try
        {
            // Resolved per request because the writer's actor services (current user, tenant) may be scoped.
            var writer = context.RequestServices.GetRequiredService<IAuditLogWriter<TContext>>();
            // CancellationToken.None, not context.RequestAborted: the denial record must not depend on the
            // client staying connected (a reset TCP/HTTP2 connection would otherwise cancel the write and lose
            // the audit trail for exactly the probing traffic it exists to catch). The store's own command and
            // connection timeouts bound how long this can run.
            await writer.WriteAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031, ERP022 // Boundary: a failed audit write must not turn a 401/403 into a 500, so it is logged and the denial proceeds.
        catch (Exception ex)
        {
            LogAuditWriteFailed(logger, ex, request.Action, (endpoint as RouteEndpoint)?.RoutePattern.RawText);
        }
#pragma warning restore CA1031, ERP022
    }

    private static List<string> _GetPolicyNames(Endpoint? endpoint)
    {
        if (endpoint is null)
        {
            return [];
        }

        return
        [
            .. endpoint
                .Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(authorizeData => authorizeData.Policy)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "AuthorizationDenialAuditWriteFailed",
        Level = LogLevel.Error,
        Message = "Failed to write the {Action} audit entry for route {Route}; the denial response proceeds unaudited."
    )]
    private static partial void LogAuditWriteFailed(ILogger logger, Exception exception, string action, string? route);
}
