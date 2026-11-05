// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// The endpoints that gather one answer from every module: <c>GET api/needs-you</c>, <c>GET api/overview</c>,
/// <c>GET api/resolve?q=</c> (a pasted id), and <c>GET api/related?module=&amp;kind=&amp;id=</c> (one object's related
/// objects). Each lists only the modules the user may view; a module that times out or fails answers
/// <c>stale: true</c> in its place.
/// </summary>
internal static class AggregationEndpoints
{
    /// <summary>The count past which the shell stops and reports <c>capped: true</c>.</summary>
    internal const long CountCap = 10_000;

    /// <summary>The longest pasted id the resolver accepts; ids are short, and the query reaches every module.</summary>
    private const int _MaxQueryLength = 256;

    /// <summary>The Overview draws at most this many figures per module.</summary>
    private const int _MaxFigures = 3;

    // The shell's SPA reads these field names; the host's own JSON options must not rename them.
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the endpoints on <paramref name="dashboard"/>, the route group at the dashboard's base path.</summary>
    internal static void Map(IEndpointRouteBuilder dashboard)
    {
        // Every module also filters by the session, but the endpoints still need one first: Basic and API key open
        // every module to any signed-in user, so without it they would answer anonymous callers too.
        dashboard
            .MapGet("/api/needs-you", _Handler(_GetNeedsYouAsync))
            .RequireDashboardShellAccess(moduleId: null)
            .ExcludeFromDescription();
        dashboard
            .MapGet("/api/overview", _Handler(_GetOverviewAsync))
            .RequireDashboardShellAccess(moduleId: null)
            .ExcludeFromDescription();
        dashboard
            .MapGet("/api/resolve", _Handler(_ResolveAsync))
            .RequireDashboardShellAccess(moduleId: null)
            .ExcludeFromDescription();
        dashboard
            .MapGet("/api/related", _Handler(_GetRelatedAsync))
            .RequireDashboardShellAccess(moduleId: null)
            .ExcludeFromDescription();
    }

    // A lambda returning Task<IResult> binds to the RequestDelegate overload, which discards the result.
    private static Func<HttpContext, Task<IResult>> _Handler(
        Func<HttpContext, CancellationToken, Task<IResult>> handler
    )
    {
        return context => handler(context, context.RequestAborted);
    }

    private static async Task<IResult> _GetNeedsYouAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var user = context.User;
        var answers = await _FanOut(context)
            .CallAsync<IDashboardAttentionSource, IReadOnlyList<DashboardAttentionItem>>(
                await _AccessAsync(context).ConfigureAwait(false),
                "attention",
                (source, token) => source.GetAttentionAsync(new DashboardAttentionRequest { User = user }, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var modules = answers.Select(answer =>
            _Section(answer, items => items.Select(static item => _ToAttentionResponse(item)).ToArray())
        );

        return Results.Json(new ModulesResponse<AttentionItemResponse>([.. modules]), _JsonOptions);
    }

    private static async Task<IResult> _GetOverviewAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var user = context.User;
        var answers = await _FanOut(context)
            .CallAsync<IDashboardOverviewSource, IReadOnlyList<DashboardFigure>>(
                await _AccessAsync(context).ConfigureAwait(false),
                "overview",
                (source, token) => source.GetOverviewAsync(new DashboardOverviewRequest { User = user }, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var modules = answers.Select(answer => _Section(answer, figures => figures.Take(_MaxFigures).ToArray()));

        return Results.Json(new ModulesResponse<DashboardFigure>([.. modules]), _JsonOptions);
    }

    private static async Task<IResult> _ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.Query["q"].ToString().Trim();

        if (query.Length is 0 or > _MaxQueryLength)
        {
            return Results.BadRequest();
        }

        var user = context.User;
        var traceTemplate = _TraceTemplate(context);
        var answers = await _FanOut(context)
            .CallAsync<IDashboardObjectResolver, IReadOnlyList<DashboardObject>>(
                await _AccessAsync(context).ConfigureAwait(false),
                "resolve",
                (source, token) =>
                    source.ResolveAsync(new DashboardResolveRequest { Query = query, User = user }, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var modules = answers.Select(answer =>
            _Section(answer, objects => objects.Select(o => _ToObjectResponse(o, traceTemplate)).ToArray())
        );

        return Results.Json(new ModulesResponse<ObjectResponse>([.. modules]), _JsonOptions);
    }

    private static async Task<IResult> _GetRelatedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request.Query;
        var moduleId = request["module"].ToString();
        var kind = request["kind"].ToString();
        var id = request["id"].ToString();

        if (moduleId.Length == 0 || kind.Length == 0 || id.Length == 0)
        {
            return Results.BadRequest();
        }

        var services = context.RequestServices;
        var manifest = services.GetRequiredService<DashboardManifestProvider>().Manifest;
        var fanOut = _FanOut(context);
        var user = context.User;

        var owner = manifest.Modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));

        if (owner is not { IsRefused: false })
        {
            return Results.NotFound();
        }

        // Relating to an object the user may not see would describe that object through other modules.
        var access = await _AccessAsync(context).ConfigureAwait(false);

        if (!access.MayView(owner.Id))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var target = new ObjectRef
        {
            Module = moduleId,
            Kind = kind,
            Id = id,
        };
        var answers = await fanOut
            .CallAsync<IDashboardRelatedSource, IReadOnlyList<DashboardRelatedGroup>>(
                access,
                "related",
                (source, token) =>
                    source.GetRelatedAsync(new DashboardRelatedRequest { Target = target, User = user }, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var groups = _MergeRelated(answers, manifest.RelatedGroupIds, _TraceTemplate(context));
        var stale = answers
            .Where(static answer => answer.Stale)
            .Select(static answer => new StaleModuleResponse(answer.Module.Id, answer.Module.DisplayName))
            .ToArray();

        return Results.Json(new RelatedResponse(groups, stale), _JsonOptions);
    }

    /// <summary>
    /// Keeps the groups the host left enabled, in the host's order, and merges the groups that share a label, so two
    /// modules that both add <c>Published messages</c> show as one list.
    /// </summary>
    private static RelatedGroupResponse[] _MergeRelated(
        IReadOnlyList<ModuleAnswer<IReadOnlyList<DashboardRelatedGroup>>> answers,
        IReadOnlyList<string> enabledGroupIds,
        string? traceTemplate
    )
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < enabledGroupIds.Count; i++)
        {
            rank.TryAdd(enabledGroupIds[i], i);
        }

        var contributions = new List<(int Rank, DashboardRelatedGroup Group)>();

        foreach (var answer in answers)
        {
            if (answer.Stale || answer.Result is null)
            {
                continue;
            }

            var declared = ((IDashboardRelatedSource)answer.Module.Module).GroupIds;

            foreach (var group in answer.Result)
            {
                // A group the module never declared cannot be disabled by the host, so it is dropped like a disabled one.
                if (declared.Contains(group.Id, StringComparer.Ordinal) && rank.TryGetValue(group.Id, out var position))
                {
                    contributions.Add((position, group));
                }
            }
        }

        return
        [
            .. contributions
                .OrderBy(static contribution => contribution.Rank)
                .GroupBy(static contribution => contribution.Group.Label, StringComparer.Ordinal)
                .Select(label => new RelatedGroupResponse(
                    label.Key,
                    [.. label.Select(static contribution => contribution.Group.Id)],
                    [
                        .. label.SelectMany(contribution =>
                            contribution.Group.Items.Select(o => _ToObjectResponse(o, traceTemplate))
                        ),
                    ],
                    label.Any(static contribution => contribution.Group.HasMore)
                )),
        ];
    }

    private static ModuleSection<TItem> _Section<TResult, TItem>(
        ModuleAnswer<TResult> answer,
        Func<TResult, IReadOnlyList<TItem>> project
    )
    {
        IReadOnlyList<TItem> items = answer is { Stale: false, Result: { } result } ? project(result) : [];

        return new ModuleSection<TItem>(answer.Module.Id, answer.Module.DisplayName, answer.Stale, items);
    }

    private static AttentionItemResponse _ToAttentionResponse(DashboardAttentionItem item)
    {
        var (count, capped) = CapCount(item.Count);

        return new AttentionItemResponse(
            item.Id,
            item.GroupKey,
            item.Title,
            item.Severity == DashboardAttentionSeverity.Critical ? "critical" : "warning",
            count,
            capped,
            item.Target,
            item.Since
        );
    }

    private static ObjectResponse _ToObjectResponse(DashboardObject value, string? traceTemplate)
    {
        return new ObjectResponse(
            value.Target,
            value.Title,
            value.Status,
            value.TraceId,
            TraceLink.Build(traceTemplate, value.TraceId)
        );
    }

    /// <summary>
    /// Stops a count at <see cref="CountCap"/>: a count above it is reported as the cap with <c>capped</c> set, so the
    /// shell shows <c>10,000+</c>.
    /// </summary>
    internal static (long Count, bool Capped) CapCount(long count)
    {
        return count > CountCap ? (CountCap, true) : (count, false);
    }

    // The per-module access the session reports to the page, so a module the page hides is never called here.
    private static Task<DashboardSessionSnapshot> _AccessAsync(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<DashboardPermissions>().GetSessionSnapshotAsync(context);
    }

    private static ModuleFanOut _FanOut(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<ModuleFanOut>();
    }

    private static string? _TraceTemplate(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value.TraceLinkTemplate;
    }

    private sealed record ModulesResponse<TItem>(IReadOnlyList<ModuleSection<TItem>> Modules);

    private sealed record ModuleSection<TItem>(string Id, string DisplayName, bool Stale, IReadOnlyList<TItem> Items);

    private sealed record AttentionItemResponse(
        string Id,
        string GroupKey,
        string Title,
        string Severity,
        long Count,
        bool Capped,
        ObjectRef? Target,
        DateTimeOffset? Since
    );

    private sealed record ObjectResponse(
        ObjectRef Target,
        string Title,
        string? Status,
        string? TraceId,
        string? TraceUrl
    );

    private sealed record RelatedResponse(
        IReadOnlyList<RelatedGroupResponse> Groups,
        IReadOnlyList<StaleModuleResponse> Stale
    );

    private sealed record RelatedGroupResponse(
        string Label,
        IReadOnlyList<string> Ids,
        IReadOnlyList<ObjectResponse> Items,
        bool HasMore
    );

    private sealed record StaleModuleResponse(string Id, string DisplayName);
}
