// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Makes a request that carries a session id and a command id the ambient command of everything it calls, so the
/// writes it commits are acknowledged on its client's streams, and tells the client in a response header whether
/// any write was.
/// </summary>
internal sealed class LiveCommandMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        var command = LiveCommandContext.TryCreate(
            LiveCaller.SubscriberOf(httpContext),
            httpContext.Request.Headers[LiveCommandHeaders.Command].ToString()
        );

        if (command is null)
        {
            await next(httpContext).ConfigureAwait(false);

            return;
        }

        var response = httpContext.Response;

        // The latest moment a header can still be set, so a write committed anywhere in the handler counts.
        response.OnStarting(() =>
        {
            response.Headers[LiveCommandHeaders.Acknowledgement] = command.Announced
                ? LiveCommandHeaders.AcknowledgementPending
                : LiveCommandHeaders.AcknowledgementNone;

            return Task.CompletedTask;
        });

        // Entered in this frame, before the await: the value then flows into the rest of the pipeline, and this
        // async method keeps it from reaching the caller and the connection's next request.
        command.Enter();

        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            command.End();
        }
    }
}
