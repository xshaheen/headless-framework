// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard;

/// <summary>
/// One open live stream: replays or resets from where the browser left off, then sends every ring event the user may
/// see and subscribed to, a heartbeat every interval, and closes when its session must be checked again.
/// </summary>
/// <remarks>
/// <para>
/// Shell events are <c>subscribed</c> (the topics now live), <c>reset</c> (refetch every view: events were missed),
/// <c>heartbeat</c>, <c>module.stale</c>, and <c>attention.changed</c>; a module's change is a <c>change</c> event.
/// Every event a ring position stands behind carries the id <c>{epoch}:{sequence}</c>, heartbeats included, so a
/// reconnect resumes from the last position this stream reached, even through events it filtered out.
/// </para>
/// <para>
/// A change event goes only to a user who may view its module and whose topics include <c>{module}.{kind}</c>, and its
/// projection passes the module's payload formatter first. <c>module.stale</c> and <c>attention.changed</c> go to
/// every user who may view the module.
/// </para>
/// </remarks>
internal sealed class StreamConnection(
    StreamConnectionSettings settings,
    EventRingBuffer buffer,
    NodeEpoch epoch,
    DashboardPayloadFormatting formatting,
    TimeProvider timeProvider,
    ILogger logger
)
{
    internal const string SubscribedEvent = "subscribed";
    internal const string ResetEvent = "reset";
    internal const string HeartbeatEvent = "heartbeat";
    internal const string ChangeEvent = "change";
    internal const string ModuleStaleEvent = "module.stale";
    internal const string AttentionChangedEvent = "attention.changed";

    /// <summary>The most ring events one read hands the stream before it writes them.</summary>
    private const int _ReadBatch = 256;

    private const string _EmptyData = "{}";

    // The shell's SPA reads these field names; the host's own JSON options must not rename them.
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs the stream until the browser leaves, the host stops, or the session needs a check.</summary>
    public async IAsyncEnumerable<SseItem<string>> RunAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var lease = settings.Lease;
        using var deadline = settings.CloseAfter is { } closeAfter
            ? new CancellationTokenSource(closeAfter > TimeSpan.Zero ? closeAfter : TimeSpan.Zero, timeProvider)
            : new CancellationTokenSource();
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            settings.RequestAborted,
            settings.Stopping,
            deadline.Token
        );
        using var heartbeat = new PeriodicTimer(settings.HeartbeatInterval, timeProvider);
        var token = closing.Token;

        if (token.IsCancellationRequested)
        {
            yield break;
        }

        var position = settings.Start.Position;
        yield return _Item(_Serialize(new SubscribedData(settings.Topics)), SubscribedEvent, position);

        if (settings.Start.Reset)
        {
            yield return _Item(_EmptyData, ResetEvent, position);
        }

        // Modules already stale when the stream opened reported it before this stream's position.
        foreach (var moduleId in settings.StaleModules)
        {
            if (settings.ViewableModules.Contains(moduleId))
            {
                yield return _Item(_Serialize(new StaleData(moduleId, Stale: true)), ModuleStaleEvent, position);
            }
        }

        Task<bool>? tick = null;

        while (!token.IsCancellationRequested)
        {
            var read = buffer.ReadAfter(position, _ReadBatch);

            if (read.Missed)
            {
                // This stream fell further behind than the ring holds; the browser refetches instead.
                position = read.Head;
                yield return _Item(_EmptyData, ResetEvent, position);
                continue;
            }

            foreach (var item in read.Events)
            {
                position = item.Sequence;
                var sse = await _ToItemAsync(item, token).ConfigureAwait(false);

                if (sse is { } visible)
                {
                    yield return visible;
                }
            }

            if (read.Events.Count > 0)
            {
                continue;
            }

            tick ??= heartbeat.WaitForNextTickAsync(CancellationToken.None).AsTask();
            var wake = await _WaitAsync(tick, buffer.WaitForAppendAsync(position), token).ConfigureAwait(false);

            if (wake == Wake.Closed)
            {
                yield break;
            }

            if (wake == Wake.Heartbeat)
            {
                tick = null;
                yield return _Item(_EmptyData, HeartbeatEvent, position);
            }
        }
    }

    private async ValueTask<SseItem<string>?> _ToItemAsync(StreamEvent item, CancellationToken cancellationToken)
    {
        if (item.Type == StreamEventType.Gap || !settings.ViewableModules.Contains(item.ModuleId))
        {
            return null;
        }

        if (item.Type == StreamEventType.AttentionChanged)
        {
            return _Item(_Serialize(new ModuleData(item.ModuleId)), AttentionChangedEvent, item.Sequence);
        }

        if (item.Type == StreamEventType.ModuleStale)
        {
            return _Item(_Serialize(new StaleData(item.ModuleId, item.Stale)), ModuleStaleEvent, item.Sequence);
        }

        if (item.Change is not { } change)
        {
            return null;
        }

        var target = change.Target;
        var topic = Topic(target);

        if (!settings.Topics.Contains(topic, StringComparer.Ordinal))
        {
            return null;
        }

        var projection = await _FormatAsync(change, cancellationToken).ConfigureAwait(false);
        var data = new ChangeData(target.Module, target.Kind, target.Id, topic, change.Version, projection);

        return _Item(_Serialize(data), ChangeEvent, item.Sequence);
    }

    /// <summary>The topic of <paramref name="target"/>'s changes: <c>{module}.{kind}</c>.</summary>
    internal static string Topic(ObjectRef target) => target.Module + "." + target.Kind;

    // The projection is a payload like any other: redaction in the browser is not redaction, so it passes the module's
    // formatter for this user, and a projection the formatter cannot turn back into JSON is withheld, which makes the
    // view refetch the object instead.
    private async ValueTask<JsonElement?> _FormatAsync(DashboardChange change, CancellationToken cancellationToken)
    {
        if (change.Projection is not { } projection)
        {
            return null;
        }

        var raw = new DashboardPayload { Content = projection.GetRawText(), ContentType = "application/json" };

        try
        {
            var formatted = await formatting
                .FormatAsync(change.Target, raw, settings.User, cancellationToken)
                .ConfigureAwait(false);

            if (ReferenceEquals(formatted, raw))
            {
                return projection;
            }

            using var document = JsonDocument.Parse(formatted.Content);

            return document.RootElement.Clone();
        }
        catch (Exception e)
        {
            // Failing closed: the event still tells the view to refetch, and the raw projection never leaves. A
            // cancelled format means the stream is closing, which the stream's own loop then notices.
            if (!cancellationToken.IsCancellationRequested)
            {
                logger.ProjectionFormatFailed(e, change.Target.Module, change.Target.Kind);
            }

            return null;
        }
    }

    private SseItem<string> _Item(string data, string eventType, long position)
    {
        return new SseItem<string>(data, eventType) { EventId = epoch.FormatId(position) };
    }

    private static string _Serialize<T>(T data) => JsonSerializer.Serialize(data, _JsonOptions);

    private static async Task<Wake> _WaitAsync(Task<bool> tick, Task appended, CancellationToken cancellationToken)
    {
        try
        {
            // The heartbeat goes first, so a stream busy with events still sends one every interval.
            var woke = await Task.WhenAny(tick, appended).WaitAsync(cancellationToken).ConfigureAwait(false);

            return woke == tick ? Wake.Heartbeat : Wake.Appended;
        }
        catch (OperationCanceledException)
        {
            return Wake.Closed;
        }
    }

    private enum Wake
    {
        Appended,
        Heartbeat,
        Closed,
    }

    private sealed record SubscribedData(IReadOnlyList<string> Topics);

    private sealed record ModuleData(string Module);

    private sealed record StaleData(string Module, bool Stale);

    private sealed record ChangeData(
        string Module,
        string Kind,
        string Id,
        string Topic,
        string Version,
        JsonElement? Projection
    );
}

/// <summary>What one stream was opened with.</summary>
internal sealed record StreamConnectionSettings
{
    /// <summary>Gets where the stream starts in the ring, and whether it starts with <c>reset</c>.</summary>
    public required StreamStart Start { get; init; }

    /// <summary>Gets the subscribed topics whose module the user may view.</summary>
    public required IReadOnlyList<string> Topics { get; init; }

    /// <summary>Gets the modules the user may view.</summary>
    public required IReadOnlySet<string> ViewableModules { get; init; }

    /// <summary>Gets the modules whose change reads were failing when the stream opened.</summary>
    public required IReadOnlyList<string> StaleModules { get; init; }

    /// <summary>Gets the signed-in user, for whom projections are formatted.</summary>
    public required ClaimsPrincipal User { get; init; }

    /// <summary>
    /// Gets how long the stream may stay open before its session must be checked again, or <see langword="null"/>
    /// when nothing bounds it.
    /// </summary>
    public required TimeSpan? CloseAfter { get; init; }

    /// <summary>Gets how often the stream sends a heartbeat.</summary>
    public required TimeSpan HeartbeatInterval { get; init; }

    /// <summary>Gets the stream's hold on the change readers, released when the stream ends.</summary>
    public required IDisposable Lease { get; init; }

    /// <summary>Gets the token cancelled when the browser leaves.</summary>
    public required CancellationToken RequestAborted { get; init; }

    /// <summary>Gets the token cancelled when the host starts to stop.</summary>
    public required CancellationToken Stopping { get; init; }
}

/// <summary>Where a stream starts in the ring.</summary>
/// <param name="Position">The sequence after which the stream sends events.</param>
/// <param name="Reset">Whether the stream first tells the browser to refetch, because it cannot resume.</param>
internal sealed record StreamStart(long Position, bool Reset);

internal static partial class StreamConnectionLog
{
    [LoggerMessage(
        EventId = 11,
        EventName = "DashboardProjectionFormatFailed",
        Level = LogLevel.Warning,
        Message = "Dashboard module {ModuleId} failed to format a {Kind} projection for a live stream; the event "
            + "is sent without it"
    )]
    internal static partial void ProjectionFormatFailed(
        this ILogger logger,
        Exception exception,
        string moduleId,
        string kind
    );
}
