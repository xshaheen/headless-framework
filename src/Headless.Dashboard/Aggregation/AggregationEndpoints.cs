// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// The endpoints that gather one answer from every module: <c>GET api/needs-you</c>, <c>GET api/overview</c>,
/// <c>GET api/resolve?q=</c> (a pasted id), <c>GET api/related?module=&amp;kind=&amp;id=</c> (one object's related
/// objects), and <c>GET api/counts?product=</c> (one product's sidebar counts). Each lists only the modules the user
/// may view; a module that times out or fails answers <c>stale: true</c> in its place, and a module that answers without
/// some of its sources names them in <c>unavailable</c>.
/// </summary>
internal static class AggregationEndpoints
{
    /// <summary>The count past which the shell stops and reports <c>capped: true</c>.</summary>
    internal const long CountCap = 10_000;

    /// <summary>The longest pasted id the resolver accepts; ids are short, and the query reaches every module.</summary>
    private const int _MaxQueryLength = 256;

    /// <summary>The Overview draws at most this many figures per module.</summary>
    private const int _MaxFigures = 3;

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
        dashboard
            .MapGet("/api/counts", _Handler(_GetCountsAsync))
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
            .CallAsync<IDashboardAttentionSource, DashboardAttentionAnswer>(
                await _AccessAsync(context).ConfigureAwait(false),
                "attention",
                (source, token) => source.GetAttentionAnswerAsync(new DashboardAttentionRequest { User = user }, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var modules = new List<ModuleSection<AttentionItemResponse>>(answers.Count);

        foreach (var answer in answers)
        {
            var commands = await _RecommendedCommandsAsync(context, answer).ConfigureAwait(false);
            modules.Add(
                _Section(
                    answer,
                    result => result.Items.Select(item => _ToAttentionResponse(item, commands)).ToArray(),
                    result => result.UnavailableSources
                )
            );
        }

        return Results.Json(new ModulesResponse<AttentionItemResponse>(modules), ShellJson.Web);
    }

    private static async Task<IResult> _GetOverviewAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = new DashboardOverviewRequest { User = context.User };
        var answers = await _FanOut(context)
            .CallAsync<IDashboardOverviewSource, DashboardOverviewAnswer>(
                await _AccessAsync(context).ConfigureAwait(false),
                "overview",
                // One call per module under one timeout, so a module's figures and strip go stale together.
                (source, token) => source.GetOverviewAnswerAsync(request, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var modules = answers.Select(answer =>
            answer is { Stale: false, Result: { } overview }
                ? new OverviewSection(
                    answer.Module.Id,
                    answer.Module.DisplayName,
                    Stale: false,
                    [.. overview.Figures.Take(_MaxFigures)],
                    overview.Tracks,
                    _Unavailable(overview.UnavailableSources)
                )
                : new OverviewSection(
                    answer.Module.Id,
                    answer.Module.DisplayName,
                    answer.Stale,
                    [],
                    [],
                    Unavailable: null
                )
        );

        return Results.Json(new OverviewResponse([.. modules]), ShellJson.Web);
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
            _Section(
                answer,
                objects => objects.Select(o => _ToObjectResponse(o, traceTemplate)).ToArray(),
                static _ => []
            )
        );

        return Results.Json(new ModulesResponse<ObjectResponse>([.. modules]), ShellJson.Web);
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

        return Results.Json(new RelatedResponse(groups, stale), ShellJson.Web);
    }

    /// <summary>
    /// Answers every sidebar count of one product in one response, so the shell draws the sidebar's counts from one
    /// request. Two modules of one product that answer the same key keep the first registered module's count.
    /// </summary>
    private static async Task<IResult> _GetCountsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var productId = context.Request.Query["product"].ToString();

        if (productId.Length == 0)
        {
            return Results.BadRequest();
        }

        var manifest = context.RequestServices.GetRequiredService<DashboardManifestProvider>().Manifest;

        if (DashboardProducts.Find(productId) is not { } product)
        {
            return Results.NotFound();
        }

        var members = manifest.Modules.Where(module => module.Product == product).ToArray();

        // A product no registered module joins has no tab, so it answers as an unknown one.
        if (members.Length == 0)
        {
            return Results.NotFound();
        }

        var access = await _AccessAsync(context).ConfigureAwait(false);

        if (!members.Any(module => access.MayView(module.Id)))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var request = new DashboardCountsRequest { User = context.User };
        var answers = await _FanOut(context)
            .CallAsync<IDashboardCountSource, IReadOnlyDictionary<string, DashboardCount>>(
                access,
                "counts",
                module => module.Product == product,
                (source, token) => source.GetCountsAsync(request, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var counts = new Dictionary<string, CountResponse>(StringComparer.Ordinal);

        foreach (var answer in answers)
        {
            if (answer is not { Stale: false, Result: { } result })
            {
                continue;
            }

            foreach (var (key, value) in result)
            {
                var (count, capped) = CapCount(value.Count);
                counts.TryAdd(
                    key,
                    new CountResponse(count, capped || value.Capped, _Unavailable(value.UnavailableSources))
                );
            }
        }

        var stale = answers
            .Where(static answer => answer.Stale)
            .Select(static answer => new StaleModuleResponse(answer.Module.Id, answer.Module.DisplayName))
            .ToArray();

        return Results.Json(new CountsResponse(product.Id, counts, stale), ShellJson.Web);
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
        Func<TResult, IReadOnlyList<TItem>> project,
        Func<TResult, IReadOnlyList<string>> unavailable
    )
    {
        if (answer is not { Stale: false, Result: { } result })
        {
            return new ModuleSection<TItem>(
                answer.Module.Id,
                answer.Module.DisplayName,
                answer.Stale,
                [],
                Unavailable: null
            );
        }

        return new ModuleSection<TItem>(
            answer.Module.Id,
            answer.Module.DisplayName,
            Stale: false,
            project(result),
            _Unavailable(unavailable(result))
        );
    }

    // Left out of the answer when every source answered, so a module that never reports one answers as it always has.
    private static IReadOnlyList<string>? _Unavailable(IReadOnlyList<string>? sources)
    {
        return sources is null or { Count: 0 } ? null : sources;
    }

    /// <summary>
    /// Describes each command the module's items recommend, as <c>GET {module}/commands</c> does for this user, so the
    /// shell draws the button, its refusal, and its Operation confirm without another request. A command the module
    /// cannot run over a filter, or one the host disabled or hid from the UI, is left out, so its item offers none.
    /// </summary>
    private static async Task<Dictionary<string, JsonElement>> _RecommendedCommandsAsync(
        HttpContext context,
        ModuleAnswer<DashboardAttentionAnswer> answer
    )
    {
        var described = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (
            answer is not { Stale: false, Result.Items: { } items }
            || answer.Module.Module is not IDashboardOperationRunner
        )
        {
            return described;
        }

        foreach (var item in items)
        {
            if (
                item is not { RecommendedCommandId: { } commandId, GroupFilter: not null }
                || described.ContainsKey(commandId)
            )
            {
                continue;
            }

            var command = answer.Module.ServerCommands.FirstOrDefault(c =>
                string.Equals(c.Id, commandId, StringComparison.Ordinal) && c.Visibility.HasFlag(DashboardChannels.Ui)
            );

            if (command is null)
            {
                continue;
            }

            var refusal = await CommandAuthorization
                .GetRefusalAsync(context, answer.Module.Id, command)
                .ConfigureAwait(false);
            described[commandId] = JsonSerializer.SerializeToElement(
                CommandDefinition.Create(command, supportsFilter: true, refusal),
                CommandEndpoints.JsonOptions
            );
        }

        return described;
    }

    private static AttentionItemResponse _ToAttentionResponse(
        DashboardAttentionItem item,
        Dictionary<string, JsonElement> commands
    )
    {
        var (count, capped) = CapCount(item.Count);
        var recommended =
            item is { RecommendedCommandId: { } commandId, GroupFilter: { } filter }
            && commands.TryGetValue(commandId, out var command)
                ? new RecommendedCommandResponse(command, filter)
                : null;

        return new AttentionItemResponse(
            item.Id,
            item.GroupKey,
            item.Title,
            item.Severity == DashboardAttentionSeverity.Critical ? "critical" : "warning",
            count,
            capped,
            item.Target,
            item.Since,
            item.LastSeenAt,
            item.RecentCount,
            recommended
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

    /// <summary>
    /// One module's part of an answer: <c>stale</c> when the whole module failed, and <c>unavailable</c> naming the
    /// sources that did not answer when the module answered in part.
    /// </summary>
    private sealed record ModuleSection<TItem>(
        string Id,
        string DisplayName,
        bool Stale,
        IReadOnlyList<TItem> Items,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Unavailable
    );

    private sealed record OverviewResponse(IReadOnlyList<OverviewSection> Modules);

    /// <summary>A module's Overview: its figures as <c>items</c>, as every section names them, and its strip tracks.</summary>
    private sealed record OverviewSection(
        string Id,
        string DisplayName,
        bool Stale,
        IReadOnlyList<DashboardFigure> Items,
        IReadOnlyList<DashboardScheduleTrack> Tracks,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Unavailable
    );

    private sealed record AttentionItemResponse(
        string Id,
        string GroupKey,
        string Title,
        string Severity,
        long Count,
        bool Capped,
        ObjectRef? Target,
        DateTimeOffset? Since,
        DateTimeOffset? LastSeenAt,
        long RecentCount,
        RecommendedCommandResponse? Recommended
    );

    /// <summary>The command an item recommends, described for the user, and the filter that scopes its Operation.</summary>
    private sealed record RecommendedCommandResponse(JsonElement Command, JsonElement Filter);

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

    private sealed record CountsResponse(
        string Product,
        IReadOnlyDictionary<string, CountResponse> Counts,
        IReadOnlyList<StaleModuleResponse> Stale
    );

    /// <summary>One count; <c>unavailable</c> names the sources it leaves out, so the shell shows <c>N+</c>.</summary>
    private sealed record CountResponse(
        long Count,
        bool Capped,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Unavailable
    );
}
