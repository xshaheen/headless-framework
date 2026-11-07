// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Dashboard;

/// <summary>
/// The built-in diagnostic module: a store of fake runs that exercises every module capability end to end, so the
/// module contract is proven before a feature module depends on it.
/// </summary>
/// <remarks>
/// <para>Its own API, at <c>{base}/api/{id}</c>:</para>
/// <list type="bullet">
///   <item><description><c>GET runs?status=&amp;error=&amp;limit=</c>: the runs, newest first, with a capped count.</description></item>
///   <item><description><c>GET runs/{runId}</c>: one run with its timeline, or <c>404</c>.</description></item>
///   <item><description><c>POST runs</c>: adds a queued run; an operator action.</description></item>
/// </list>
/// <para>
/// Its object kind is <c>run</c>, so its stream topic is <c>{id}.run</c>, and its commands are <c>{id}.fail</c>,
/// <c>{id}.requeue</c>, and <c>{id}.add-note</c>.
/// </para>
/// </remarks>
internal sealed class DiagnosticsModule
    : IDashboardModule,
        IDashboardAttentionSource,
        IDashboardOverviewSource,
        IDashboardObjectResolver,
        IDashboardRelatedSource,
        IDashboardPayloadSource,
        IDashboardPayloadFormatter,
        IDashboardChangeSource,
        IDashboardCommandSource,
        IDashboardOperationRunner
{
    /// <summary>The most runs one list call returns.</summary>
    internal const int MaxListLimit = 200;

    /// <summary>The count past which a list stops counting and reports its count as capped.</summary>
    internal const int CountCap = 10_000;

    private const int _DefaultListLimit = 50;

    private readonly DiagnosticsStore _store;
    private readonly IDashboardEventPublisher _publisher;
    private readonly DiagnosticsSources _sources;
    private readonly DiagnosticsCommands _commands;

    public DiagnosticsModule(
        string id,
        DiagnosticsStore store,
        IDashboardEventPublisher publisher,
        TimeProvider timeProvider
    )
    {
        Id = Argument.IsNotNullOrWhiteSpace(id);
        _store = Argument.IsNotNull(store);
        _publisher = Argument.IsNotNull(publisher);
        Argument.IsNotNull(timeProvider);

        DisplayName = string.Equals(id, DiagnosticsOptions.DefaultId, StringComparison.Ordinal)
            ? "Diagnostics"
            : $"Diagnostics ({id})";
        Assets = DiagnosticsAssets.Create(id);
        _sources = new DiagnosticsSources(id, store, timeProvider);
        _commands = new DiagnosticsCommands(id, store, _sources, publisher);
        GroupIds = [_sources.LinkedRunsGroupId, _sources.SameTraceGroupId];
    }

    public string Id { get; }

    public string DisplayName { get; }

    public DashboardModuleAssets Assets { get; }

    public IReadOnlyList<string> GroupIds { get; }

    public IReadOnlyList<string> ObjectKinds { get; } = [DiagnosticsSources.RunKind];

    public IReadOnlyList<DashboardCommand> Commands => _commands.Commands;

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        endpoints.Routes.MapGet("/runs", _ListRuns);
        endpoints.Routes.MapGet("/runs/{runId}", _GetRun);
        endpoints.RequireOperate(endpoints.Routes.MapPost("/runs", _AddRun));
    }

    public Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(_sources.GetAttention());
    }

    public Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(_sources.GetOverview());
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_sources.Resolve(request.Query));
    }

    public Task<IReadOnlyList<DashboardRelatedGroup>> GetRelatedAsync(
        DashboardRelatedRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_sources.GetRelated(request.Target));
    }

    public Task<DashboardPayload?> GetPayloadAsync(DashboardPayloadRequest request, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_sources.GetPayload(request.Target));
    }

    public ValueTask<DashboardPayload> FormatAsync(
        DashboardPayloadFormatRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return ValueTask.FromResult(DiagnosticsSources.Format(request.Payload));
    }

    public Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_sources.GetStartCursor());
    }

    public Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(cursor);

        return Task.FromResult(_sources.ReadChanges(cursor));
    }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_commands.GetAllowed(request.Targets));
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_commands.Execute(request));
    }

    public Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_commands.Prepare(request));
    }

    public Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return Task.FromResult(_commands.RunChunk(request));
    }

    private JsonHttpResult<DiagnosticsRunList> _ListRuns(string? status, string? error, int? limit)
    {
        var filter = DiagnosticsRunFilter.Create(status, error);
        var take = Math.Clamp(limit ?? _DefaultListLimit, 1, MaxListLimit);
        var count = 0;
        var items = new List<DiagnosticsRunSummary>(take);

        foreach (var run in _store.Runs.Where(filter.Matches))
        {
            if (count == CountCap)
            {
                return _List(items, count, capped: true);
            }

            count++;

            if (items.Count < take)
            {
                items.Add(DiagnosticsRunSummary.From(run));
            }
        }

        return _List(items, count, capped: false);
    }

    private Results<JsonHttpResult<DiagnosticsRunDetail>, NotFound> _GetRun(string runId)
    {
        return _store.Find(runId) is { } run
            ? TypedResults.Json(DiagnosticsRunDetail.From(run), DiagnosticsJsonContext.Default.DiagnosticsRunDetail)
            : TypedResults.NotFound();
    }

    private JsonHttpResult<DiagnosticsRunDetail> _AddRun()
    {
        var run = _store.AddRun();
        _publisher.Publish(_sources.ToChange(run, affectsAttention: false));

        return TypedResults.Json(
            DiagnosticsRunDetail.From(run),
            DiagnosticsJsonContext.Default.DiagnosticsRunDetail,
            statusCode: StatusCodes.Status201Created
        );
    }

    private static JsonHttpResult<DiagnosticsRunList> _List(List<DiagnosticsRunSummary> items, int count, bool capped)
    {
        return TypedResults.Json(
            new DiagnosticsRunList(items, count, capped),
            DiagnosticsJsonContext.Default.DiagnosticsRunList
        );
    }
}

/// <summary>A run as a list row and a live change's projection show it.</summary>
internal sealed record DiagnosticsRunSummary(
    string Id,
    DiagnosticsRunStatus Status,
    int Attempts,
    string? LastError,
    string TraceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version
)
{
    public static DiagnosticsRunSummary From(DiagnosticsRun run)
    {
        return new DiagnosticsRunSummary(
            run.Id,
            run.Status,
            run.Attempts,
            run.LastError,
            run.TraceId,
            run.CreatedAt,
            run.UpdatedAt,
            run.Version.ToString(CultureInfo.InvariantCulture)
        );
    }
}

/// <summary>A run as the inspector and run page show it: the list row's fields and its timeline.</summary>
internal sealed record DiagnosticsRunDetail(
    string Id,
    DiagnosticsRunStatus Status,
    int Attempts,
    string? LastError,
    string TraceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version,
    IReadOnlyList<DiagnosticsRunEvent> Timeline
)
{
    public static DiagnosticsRunDetail From(DiagnosticsRun run)
    {
        return new DiagnosticsRunDetail(
            run.Id,
            run.Status,
            run.Attempts,
            run.LastError,
            run.TraceId,
            run.CreatedAt,
            run.UpdatedAt,
            run.Version.ToString(CultureInfo.InvariantCulture),
            run.Timeline
        );
    }
}

/// <summary>One page of runs, and how many runs match, counted no further than <see cref="DiagnosticsModule.CountCap"/>.</summary>
internal sealed record DiagnosticsRunList(IReadOnlyList<DiagnosticsRunSummary> Items, int Count, bool CountCapped);

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(DiagnosticsRunSummary))]
[JsonSerializable(typeof(DiagnosticsRunDetail))]
[JsonSerializable(typeof(DiagnosticsRunList))]
internal sealed partial class DiagnosticsJsonContext : JsonSerializerContext;
