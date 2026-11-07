// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// <c>GET api/stream?topics=</c>: the browser's one live stream, as server-sent events. Topics are
/// <c>{module}.{kind}</c>, comma-separated or repeated. The last event id comes from the <c>Last-Event-ID</c> header,
/// or the <c>lastEventId</c> query parameter, which a new <c>EventSource</c> needs because it cannot set the header.
/// </summary>
/// <remarks>
/// <para>
/// The stream closes at the earliest of the session ticket's expiry; its next revalidation under host authentication
/// with a revalidation callback; and <see cref="CredentialSessionBound"/> under Basic and API-key authentication. The
/// browser reconnects with its last event id, so the reconnect runs the cookie validation, the credential check, and
/// the per-module permissions again, and a user still allowed loses nothing.
/// </para>
/// <para>
/// A request whose <c>Origin</c> is foreign is refused with <c>403</c>, and a stream never delays the host's
/// shutdown.
/// </para>
/// </remarks>
internal static class StreamEndpoint
{
    /// <summary>
    /// How long a stream stays open under Basic and API-key authentication before its reconnect checks the credential
    /// again.
    /// </summary>
    internal static readonly TimeSpan CredentialSessionBound = TimeSpan.FromMinutes(5);

    /// <summary>The most topics one stream subscribes to.</summary>
    internal const int MaxTopics = 64;

    private const int _MaxTopicLength = 200;

    private const string _AuthenticationName = SetupDashboard.AuthenticationName;

    /// <summary>Maps the endpoint on <paramref name="dashboard"/>, the group at the dashboard's base path.</summary>
    internal static void Map(IEndpointRouteBuilder dashboard)
    {
        // Typed as a result-returning handler: a lambda over HttpContext alone binds as a RequestDelegate, which
        // would discard the IResult.
        Func<HttpContext, Task<IResult>> handler = static context => _OpenAsync(context);

        dashboard.MapGet("/api/stream", handler).RequireDashboardShellAccess(moduleId: null).ExcludeFromDescription();
    }

    private static async Task<IResult> _OpenAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var options = services.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value;

        if (!SameOriginCheck.IsAllowed(context.Request))
        {
            return Results.Text(
                SameOriginCheck.RefusalMessage(context.Request),
                "text/plain",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        var user = context.User;
        var manifest = services.GetRequiredService<DashboardManifestProvider>().Manifest;
        var access = await services
            .GetRequiredService<DashboardPermissions>()
            .GetSessionSnapshotAsync(context)
            .ConfigureAwait(false);
        var viewable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in manifest.Modules)
        {
            if (!module.IsRefused && access.MayView(module.Id))
            {
                viewable.Add(module.Id);
            }
        }

        var timeProvider = services.GetRequiredService<TimeProvider>();
        var closeAfter = await _CloseAfterAsync(context, timeProvider).ConfigureAwait(false);
        var buffer = services.GetRequiredService<EventRingBuffer>();
        var epoch = services.GetRequiredService<NodeEpoch>();

        // Opened before the start is resolved: a restart of the change readers records its gap first, so a reconnect
        // from before the restart gets reset.
        var lease = services.GetRequiredService<ChangeReaderCoordinator>().Open();
        context.Response.RegisterForDispose(lease);

        // The head is read before the stale modules: a module reported stale in between is then either in the
        // snapshot or in the ring after the stream's position.
        var start = _ResolveStart(_LastEventId(context.Request), buffer, epoch);
        var staleModules = services.GetRequiredService<DashboardEventPublisher>().StaleModules();

        var settings = new StreamConnectionSettings
        {
            Start = start,
            Topics = _ParseTopics(context.Request.Query["topics"], viewable),
            ViewableModules = viewable,
            StaleModules = staleModules,
            User = user,
            CloseAfter = closeAfter,
            HeartbeatInterval = options.HeartbeatInterval,
            Lease = lease,
            RequestAborted = context.RequestAborted,
            Stopping = services.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None,
        };

        var connection = new StreamConnection(
            settings,
            buffer,
            epoch,
            services.GetRequiredService<DashboardPayloadFormatting>(),
            timeProvider,
            services.GetRequiredService<ILogger<StreamConnection>>()
        );

        // A reverse proxy such as nginx buffers responses unless told otherwise, which would hold events back.
        context.Response.Headers["X-Accel-Buffering"] = "no";

        return TypedResults.ServerSentEvents(connection.RunAsync());
    }

    private static string? _LastEventId(HttpRequest request)
    {
        var header = request.Headers["Last-Event-ID"].ToString();

        return header.Length > 0 ? header : request.Query["lastEventId"].ToString();
    }

    private static StreamStart _ResolveStart(string? lastEventId, EventRingBuffer buffer, NodeEpoch epoch)
    {
        var head = buffer.Head;

        if (string.IsNullOrEmpty(lastEventId))
        {
            return new StreamStart(head, Reset: false);
        }

        return epoch.TryParseId(lastEventId, out var sequence) && buffer.CanResumeAfter(sequence)
            ? new StreamStart(sequence, Reset: false)
            : new StreamStart(head, Reset: true);
    }

    // A topic names a module the user may view, so a topic of a hidden or unknown module is left out of what the
    // stream reports as subscribed, as well as out of what it sends.
    private static string[] _ParseTopics(IEnumerable<string?> values, HashSet<string> viewable)
    {
        var topics = new List<string>();

        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var topic in parts)
            {
                var separator = topic.IndexOf('.', StringComparison.Ordinal);

                if (
                    topic.Length > _MaxTopicLength
                    || separator <= 0
                    || separator == topic.Length - 1
                    || !viewable.Contains(topic[..separator])
                    || topics.Contains(topic, StringComparer.Ordinal)
                )
                {
                    continue;
                }

                topics.Add(topic);

                if (topics.Count == MaxTopics)
                {
                    return [.. topics];
                }
            }
        }

        return [.. topics];
    }

    private static async Task<TimeSpan?> _CloseAfterAsync(HttpContext context, TimeProvider timeProvider)
    {
        var config = context.RequestServices.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get(_AuthenticationName);

        if (config.Mode == AuthMode.None)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        DateTimeOffset? closeAt = null;

        var session = await DashboardHostSession.AuthenticateAsync(context, _AuthenticationName).ConfigureAwait(false);

        if (session is { Succeeded: true, Properties: { } ticket })
        {
            closeAt = ticket.ExpiresUtc;

            if (
                config.Mode == AuthMode.Host
                && config.Host is { Revalidate: not null, RevalidationInterval: { } interval }
                && DashboardHostSession.GetValidatedAt(ticket) is { } validatedAt
            )
            {
                closeAt = _Earliest(closeAt, validatedAt + interval);
            }
        }

        // A script that sends the credential header holds no ticket; the bound still makes it reconnect and present
        // the credential again.
        if (config.Mode is AuthMode.Basic or AuthMode.ApiKey)
        {
            closeAt = _Earliest(closeAt, now + CredentialSessionBound);
        }

        return closeAt - now;
    }

    private static DateTimeOffset _Earliest(DateTimeOffset? current, DateTimeOffset candidate)
    {
        return current is { } value && value < candidate ? value : candidate;
    }
}
