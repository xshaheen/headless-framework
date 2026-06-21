// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Headless.Logging;

/// <summary>
/// Middleware that pushes per-request identity properties into the Serilog
/// <see cref="Serilog.Context.LogContext"/> for the duration of the HTTP request.
/// Properties written: <c>UserId</c>, <c>AccountId</c>, and <c>CorrelationId</c> — each
/// only when the corresponding value is non-null on the current <see cref="IRequestContext"/>.
/// </summary>
/// <param name="requestContext">
/// The ambient request context providing the <c>UserId</c>, <c>AccountId</c>, and <c>CorrelationId</c>
/// values pushed into the Serilog log context.
/// </param>
public sealed class SerilogEnrichersMiddleware(IRequestContext requestContext) : IMiddleware
{
    private const string _UserId = "UserId";
    private const string _AccountId = "AccountId";
    private const string _CorrelationId = "CorrelationId";

    /// <summary>
    /// Pushes identity properties and invokes the next middleware in the pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next request delegate.</param>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        using var _ = requestContext.User.UserId is { } userId ? LogContext.PushProperty(_UserId, userId) : null;
        using var __ = requestContext.User.AccountId is { } accountId
            ? LogContext.PushProperty(_AccountId, accountId)
            : null;
        using var ___ = requestContext.CorrelationId is { } correlationId
            ? LogContext.PushProperty(_CorrelationId, correlationId)
            : null;

        await next(context).ConfigureAwait(false);
    }
}
