// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics;
using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Middlewares;

/// <summary>
/// A middleware which handles <see cref="OperationCanceledException"/> caused by the HTTP request being aborted, then
/// shortcuts and returns an error status code.
/// </summary>
/// <seealso cref="IMiddleware" />
public sealed partial class RequestCanceledMiddleware(ILogger<RequestCanceledMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        Argument.IsNotNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;

            context
                .Features.Get<IHttpActivityFeature>()
                ?.Activity.AddEvent(new ActivityEvent("Client cancelled the request"));

            LogRequestCanceled(logger);
        }
    }

    [LoggerMessage(
        EventId = 5002,
        EventName = "RequestCancelled",
        Level = LogLevel.Information,
        Message = "Client cancelled the request",
        SkipEnabledCheck = false
    )]
    public static partial void LogRequestCanceled(ILogger logger);
}
