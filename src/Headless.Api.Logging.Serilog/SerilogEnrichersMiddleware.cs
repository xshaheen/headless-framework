// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Headless.Logging;

public sealed class SerilogEnrichersMiddleware(IRequestContext requestContext) : IMiddleware
{
    private const string _UserId = "UserId";
    private const string _AccountId = "AccountId";
    private const string _CorrelationId = "CorrelationId";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        using var _ = requestContext.User.UserId is { } userId ? LogContext.PushProperty(_UserId, userId) : null;
        using var __ = requestContext.User.AccountId is { } accountId
            ? LogContext.PushProperty(_AccountId, accountId)
            : null;
        using var ___ = requestContext.CorrelationId is { } correlationId
            ? LogContext.PushProperty(_CorrelationId, correlationId)
            : null;

        await next(context).AnyContext();
    }
}
