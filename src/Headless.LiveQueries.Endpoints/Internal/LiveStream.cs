// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Api;
using Headless.LiveQueries.Resources;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The runs of one stream. Each run executes the endpoint through a synthetic request, so ASP.NET Core binds the
/// handler's arguments and runs the endpoint's filters again, whatever shape the handler has.
/// </summary>
/// <param name="opener">The request that opened the stream.</param>
/// <param name="endpoint">The live endpoint.</param>
/// <param name="handler">The host-wide services of live endpoints.</param>
/// <param name="aborted">Cancelled when the stream ends; a run sees it as its request's abort token.</param>
internal sealed partial class LiveStream(
    HttpContext opener,
    Endpoint endpoint,
    LiveStreamHandler handler,
    CancellationToken aborted
)
{
    private static readonly byte[] _JsonNull = "null"u8.ToArray();

    private long _lastRunStarted;
    private bool _ran;

    /// <summary>Gets what the last run wrote, or <see langword="null" /> when it threw.</summary>
    public LiveRunResponse? LastResponse { get; private set; }

    /// <summary>
    /// Gets the problem that answers the opening request in place of the first run's own response, or
    /// <see langword="null" /> when that response is what the client gets.
    /// </summary>
    public ProblemDetails? OpeningProblem { get; private set; }

    public ValueTask<IDisposable?> EnterRerunAsync(CancellationToken cancellationToken)
    {
        return handler.Limiter.EnterRerunAsync(_lastRunStarted, cancellationToken);
    }

    /// <summary>
    /// Runs the endpoint once. A first run that does not succeed reports its own response, which the caller sends
    /// as it is; a later one reports a problem body, which ends the stream as its failure event.
    /// </summary>
    public async ValueTask<LiveRunResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = !_ran;
        _ran = true;
        _lastRunStarted = handler.Limiter.Now;
        LastResponse = null;

        // Both set here, in the frame that awaits the endpoint: they flow down into the handler's calls and are
        // gone when this method returns, so the frames above keep reading the opening request. The accessor is
        // what the framework's current-user accessor reads.
        var recorder = LiveQueryRecorder.Begin();
        var scope = handler.Scopes.CreateAsyncScope();
        var run = _CreateRequest(scope.ServiceProvider);
        var current = LiveRunHttpContextAccessor.Begin(run);

        try
        {
            LiveRunResponse response;

            try
            {
                await LiveRunAuthorization.AuthenticateAsync(run, opener.User).ConfigureAwait(false);
                var denied = await LiveRunAuthorization.ExecuteAsync(run, endpoint, opener.User).ConfigureAwait(false);
                response = await _ReadResponseAsync(run, denied).ConfigureAwait(false);
            }
            catch (Exception exception) when (!first && !cancellationToken.IsCancellationRequested)
            {
                // The first run's exception is the request's own and takes the host's ordinary error handling.
                // After that the response is a stream, and the failure has to travel inside it.
                LogRerunThrew(handler.Logger, exception, endpoint.DisplayName);

                return _Failure(StatusCodes.Status500InternalServerError, LiveEndpointsMessageDescriber.RunFailed());
            }

            LastResponse = response;

            // Both built-in formats copy the body into the stream as a JSON value, without escaping it. Text copied
            // that way could close the value and add members of its own to what another viewer receives, so
            // anything not declared as JSON never becomes a payload.
            if (response.Succeeded && !response.Body.IsEmpty && !response.IsJson)
            {
                LogNotJson(handler.Logger, endpoint.DisplayName, response.Headers.ContentType.ToString());

                var problem = _Problem(
                    StatusCodes.Status500InternalServerError,
                    LiveEndpointsMessageDescriber.ResponseNotJson()
                );

                if (first)
                {
                    OpeningProblem = problem;
                }

                return _Failure(problem);
            }

            if (response.Succeeded)
            {
                var dependencies = recorder.Snapshot();

                // The first run leaves this to the hub, whose exception the caller turns into a response.
                if (dependencies.IsEmpty && !first)
                {
                    return _Failure(
                        StatusCodes.Status500InternalServerError,
                        LiveEndpointsMessageDescriber.NoDependencies()
                    );
                }

                // A result with no body is still a value, and both built-in formats need a JSON token to carry.
                return new LiveRunResult(
                    response.Body.IsEmpty ? _JsonNull : response.Body,
                    Failed: false,
                    dependencies
                );
            }

            if (first)
            {
                return new LiveRunResult(response.Body, Failed: true, LiveDependencies.None);
            }

            LogRerunFailed(handler.Logger, endpoint.DisplayName, response.StatusCode);

            return response.Denied
                ? _Denied(response.StatusCode)
                : _Failure(response.StatusCode, LiveEndpointsMessageDescriber.RunFailed());
        }
        finally
        {
            // Both ended for a task the handler left running, which carries them for the rest of its life: it
            // must not keep reading a request whose scope is gone, and it is no longer part of a live query, so
            // its writes are ordinary ones.
            recorder.End();
            current.End();
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Layered over the opening request, so everything its middleware established stays visible: the endpoint, route
    // values, the resolved tenant, the connection. Replaced are only the parts a run must own. Each is set on the
    // run's own collection before anything assigns through it, because an assignment through an inherited feature
    // would change the opening request.
    private DefaultHttpContext _CreateRequest(IServiceProvider services)
    {
        var features = new FeatureCollection(opener.Features);
        features.Set(LiveRunMarker.Instance);
        features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = services });
        features.Set<IItemsFeature>(new ItemsFeature { Items = new Dictionary<object, object?>(opener.Items) });
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = aborted });
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        features.Set<IResponseCookiesFeature>(new ResponseCookiesFeature(features));

        return new DefaultHttpContext(features);
    }

    private static async Task<LiveRunResponse> _ReadResponseAsync(HttpContext run, bool denied)
    {
        // Flushes what the endpoint wrote through the response's pipe writer into the stream below.
        await run.Response.CompleteAsync().ConfigureAwait(false);
        var body = (MemoryStream)run.Features.GetRequiredFeature<IHttpResponseBodyFeature>().Stream;

        return new LiveRunResponse(run.Response.StatusCode, run.Response.Headers, body.ToArray(), denied);
    }

    private LiveRunResult _Denied(int statusCode)
    {
        var error = LiveEndpointsMessageDescriber.Unauthorized();

        var problem =
            statusCode == StatusCodes.Status401Unauthorized
                ? handler.Problems.Unauthorized(detail: error.Description, error: error)
                : handler.Problems.Forbidden(detail: error.Description, error: error);

        return _Failure(problem);
    }

    private LiveRunResult _Failure(int statusCode, ErrorDescriptor error)
    {
        return _Failure(_Problem(statusCode, error));
    }

    private ProblemDetails _Problem(int statusCode, ErrorDescriptor error)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Detail = error.Description,
            Extensions = { ["error"] = error },
        };

        handler.Problems.Normalize(problem);

        return problem;
    }

    private LiveRunResult _Failure(ProblemDetails problem)
    {
        return new LiveRunResult(
            JsonSerializer.SerializeToUtf8Bytes(problem, handler.JsonOptions),
            Failed: true,
            LiveDependencies.None
        );
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "LiveEndpointRerunThrew",
        Level = LogLevel.Error,
        Message = "Live endpoint {Endpoint} threw when it ran again; its stream ends with a failure event"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogRerunThrew(ILogger logger, Exception exception, string? endpoint);

    [LoggerMessage(
        EventId = 2,
        EventName = "LiveEndpointRerunFailed",
        Level = LogLevel.Information,
        Message = "Live endpoint {Endpoint} answered {StatusCode} when it ran again; its stream ends with a failure event"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogRerunFailed(ILogger logger, string? endpoint, int statusCode);

    [LoggerMessage(
        EventId = 3,
        EventName = "LiveEndpointResponseNotJson",
        Level = LogLevel.Error,
        Message = "Live endpoint {Endpoint} answered with content type '{ContentType}'; a live endpoint must return JSON, so the run is a failure"
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogNotJson(ILogger logger, string? endpoint, string contentType);
}
