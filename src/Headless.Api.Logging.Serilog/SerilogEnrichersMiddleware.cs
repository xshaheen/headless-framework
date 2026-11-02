// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Headless.Logging;

/// <summary>
/// Middleware that pushes per-request identity properties into the Serilog
/// <see cref="Serilog.Context.LogContext"/> for the duration of the HTTP request.
/// Properties written: <c>UserId</c>, <c>AccountId</c>, and <c>CorrelationId</c> — each
/// only when the corresponding value is non-null on the current <see cref="IRequestContext"/>.
/// </summary>
/// <param name="next">The next request delegate.</param>
/// <remarks>
/// A conventional middleware rather than a scoped <c>IMiddleware</c>: the pipeline builds it once, so a request no
/// longer pays for activating the middleware itself through the scope. <see cref="IRequestContext"/> is still
/// injected per request, so a host that replaces it keeps controlling what is logged.
/// </remarks>
public sealed class SerilogEnrichersMiddleware(RequestDelegate next)
{
    private const string _UserId = "UserId";
    private const string _AccountId = "AccountId";
    private const string _CorrelationId = "CorrelationId";

    /// <summary>
    /// Pushes identity properties and invokes the next middleware in the pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="requestContext">
    /// The ambient request context providing the <c>UserId</c>, <c>AccountId</c>, and <c>CorrelationId</c>
    /// values pushed into the Serilog log context.
    /// </param>
    public Task InvokeAsync(HttpContext context, IRequestContext requestContext)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(requestContext);

        var userId = requestContext.User.UserId;
        var accountId = requestContext.User.AccountId;
        var correlationId = requestContext.CorrelationId;

        // Anonymous requests without a correlation header (probes, static assets) push nothing, so they skip the
        // async state machine entirely.
        if (userId is null && accountId is null && correlationId is null)
        {
            return next(context);
        }

        return _InvokeWithPropertiesAsync(context, userId, accountId, correlationId);
    }

    private async Task _InvokeWithPropertiesAsync(
        HttpContext context,
        object? userId,
        object? accountId,
        string? correlationId
    )
    {
        using var _ = userId is not null ? LogContext.PushProperty(_UserId, userId) : null;
        using var __ = accountId is not null ? LogContext.PushProperty(_AccountId, accountId) : null;
        using var ___ = correlationId is not null ? LogContext.PushProperty(_CorrelationId, correlationId) : null;

        await next(context).ConfigureAwait(false);
    }
}
