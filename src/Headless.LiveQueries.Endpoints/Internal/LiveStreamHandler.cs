// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json;
using Headless.Api;
using Headless.LiveQueries.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Decides what a request to a live endpoint receives: its ordinary response, a refusal, or a stream that a
/// transport writes.
/// </summary>
internal sealed class LiveStreamHandler(
    LiveQueryHub hub,
    LiveTransportSelector transports,
    LiveStreamLimiter limiter,
    IProblemDetailsCreator problems,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    IOptions<LiveEndpointsOptions> options,
    IOptions<HttpJsonOptions> jsonOptions,
    ILogger<LiveStreamHandler> logger
)
{
    private readonly int _retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(options.Value.ReconnectDelay.TotalSeconds));

    public LiveStreamLimiter Limiter => limiter;

    public IProblemDetailsCreator Problems => problems;

    public IServiceScopeFactory Scopes => scopes;

    public JsonSerializerOptions JsonOptions => jsonOptions.Value.SerializerOptions;

    public ILogger Logger => logger;

    public async ValueTask<object?> HandleAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // The same URL answers with JSON or with a stream, so a cache must key every response of it on Accept.
        httpContext.Response.Headers.Append(HeaderNames.Vary, HeaderNames.Accept);

        var endpoint = httpContext.GetEndpoint();
        var transport = endpoint is null ? null : transports.Select(httpContext);

        if (endpoint is null || transport is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        using var admitted = limiter.TryOpen(LiveStreamLimiter.OwnerOf(httpContext));

        if (admitted is null)
        {
            return _TooManyStreams(httpContext);
        }

        // One token for everything the stream does, so a client that leaves and a host that stops end it the same
        // way.
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(
            httpContext.RequestAborted,
            lifetime.ApplicationStopping
        );

        var stream = new LiveStream(httpContext, endpoint, this, ending.Token);
        // Read once, from the request that opened the stream: the commands this client session sends are
        // acknowledged on it, and nobody else's.
        var subscriber = LiveCaller.SubscriberOf(httpContext);
        var subscription = hub.WatchAsync(stream.RunAsync, stream.EnterRerunAsync, subscriber, ending.Token)
            .GetAsyncEnumerator(ending.Token);
        var updates = new LiveUpdateSequence(subscription);

        try
        {
            return await _StreamAsync(httpContext, transport, stream, subscription, updates, ending.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ending.IsCancellationRequested)
        {
            // The client left or the host is stopping; that is how every stream ends.
            return Results.Empty;
        }
        finally
        {
            await ending.CancelAsync().ConfigureAwait(false);
            await updates.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<object?> _StreamAsync(
        HttpContext httpContext,
        ILiveTransport transport,
        LiveStream stream,
        IAsyncEnumerator<LiveUpdate> subscription,
        LiveUpdateSequence updates,
        CancellationToken cancellationToken
    )
    {
        bool noDependencies;

        try
        {
            // The first run. An exception it throws leaves through here and takes the host's ordinary error
            // handling, as it would for a request that asked for no stream.
            if (!await subscription.MoveNextAsync().ConfigureAwait(false))
            {
                return Results.Empty;
            }

            noDependencies = false;
        }
        catch (LiveQueryHasNoDependenciesException)
        {
            noDependencies = true;
        }

        if (noDependencies)
        {
            var error = LiveEndpointsMessageDescriber.NoDependencies();

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Detail = error.Description,
                Extensions = { ["error"] = error },
            };

            problems.Normalize(problem);

            return Results.Problem(problem);
        }

        if (subscription.Current.Kind is LiveUpdateKind.Failed)
        {
            // The first run answered with something a stream cannot carry, which the client must not be sent.
            if (stream.OpeningProblem is { } refused)
            {
                return Results.Problem(refused);
            }

            await _CopyAsync(stream.LastResponse!, httpContext.Response, cancellationToken).ConfigureAwait(false);

            return Results.Empty;
        }

        await transport.WriteAsync(httpContext, updates, cancellationToken).ConfigureAwait(false);

        return Results.Empty;
    }

    // The first run did not succeed, so the client gets what an ordinary request would have got: the same status,
    // headers and body.
    private static async Task _CopyAsync(
        LiveRunResponse run,
        HttpResponse response,
        CancellationToken cancellationToken
    )
    {
        response.StatusCode = run.StatusCode;

        foreach (var header in run.Headers)
        {
            response.Headers[header.Key] = header.Value;
        }

        if (!response.Headers.Vary.Contains(HeaderNames.Accept, StringComparer.OrdinalIgnoreCase))
        {
            response.Headers.Append(HeaderNames.Vary, HeaderNames.Accept);
        }

        if (!run.Body.IsEmpty)
        {
            await response.Body.WriteAsync(run.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    private IResult _TooManyStreams(HttpContext httpContext)
    {
        var problem = problems.TooManyRequests(
            _retryAfterSeconds,
            error: LiveEndpointsMessageDescriber.StreamLimitReached()
        );

        httpContext.Response.Headers.RetryAfter = _retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // A shared cache must never replay a refusal to a caller who has room for a stream.
        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Problem(problem);
    }
}
