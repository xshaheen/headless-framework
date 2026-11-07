// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.Dashboard;

/// <summary>
/// The diagnostic module's read capabilities over its store: Needs you, Overview, id and trace lookups, related runs,
/// payloads and their redaction, and change reads.
/// </summary>
internal sealed class DiagnosticsSources(string moduleId, DiagnosticsStore store, TimeProvider timeProvider)
{
    /// <summary>The object kind of a run, and the second half of its stream topic <c>{module}.run</c>.</summary>
    public const string RunKind = "run";

    /// <summary>The most changes one read returns; a burst of writes is read over several intervals.</summary>
    internal const int ChangePageSize = 200;

    /// <summary>The most runs a related group lists.</summary>
    internal const int RelatedPageSize = 20;

    private const string _Redacted = "***";

    private static readonly FrozenSet<string> _SecretNames = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "apiKey",
        "x-api-key",
        "authorization",
        "password",
        "secret",
        "token"
    );

    /// <summary>Gets the id of the related group that lists runs linked to an object of any module.</summary>
    public string LinkedRunsGroupId => moduleId + ".linked-runs";

    /// <summary>Gets the id of the related group that lists the other runs that carry a run's trace id.</summary>
    public string SameTraceGroupId => moduleId + ".same-trace";

    public ObjectRef RefOf(string runId)
    {
        return new ObjectRef
        {
            Module = moduleId,
            Kind = RunKind,
            Id = runId,
        };
    }

    public DashboardObject ToObject(DiagnosticsRun run)
    {
        return new DashboardObject
        {
            Target = RefOf(run.Id),
            Title = run.Id,
            Status = run.Status.ToString(),
            TraceId = run.TraceId,
        };
    }

    /// <summary>
    /// Lists failed runs, one item per error message, the most runs first. Each item recommends
    /// <paramref name="requeueCommandId"/> over its own group, and counts the runs that failed in the last hour as its
    /// growth.
    /// </summary>
    public IReadOnlyList<DashboardAttentionItem> GetAttention(string requeueCommandId)
    {
        var hourAgo = timeProvider.GetUtcNow().AddHours(-1);

        return
        [
            .. store
                .Runs.Where(static run => run.Status == DiagnosticsRunStatus.Failed)
                .GroupBy(static run => run.LastError ?? string.Empty, StringComparer.Ordinal)
                .Select(group =>
                {
                    var runs = group.ToArray();
                    var error = group.Key.Length == 0 ? "Unknown error" : group.Key;
                    var count = runs.Length.ToString(CultureInfo.InvariantCulture);

                    return new DashboardAttentionItem
                    {
                        Id = "failed:" + group.Key,
                        GroupKey = group.Key,
                        Title = runs.Length == 1 ? $"1 run failed: {error}" : $"{count} runs failed: {error}",
                        Severity = DashboardAttentionSeverity.Critical,
                        Count = runs.Length,
                        Target = runs.Length == 1 ? RefOf(runs[0].Id) : null,
                        Since = runs.Min(static run => run.StatusChangedAt),
                        LastSeenAt = runs.Max(static run => run.StatusChangedAt),
                        RecentCount = runs.Count(run => run.StatusChangedAt > hourAgo),
                        RecommendedCommandId = requeueCommandId,
                        GroupFilter = JsonSerializer.SerializeToElement(
                            new DiagnosticsRunFilterDocument("Failed", group.Key),
                            DiagnosticsJsonContext.Default.DiagnosticsRunFilterDocument
                        ),
                    };
                })
                .OrderByDescending(static item => item.Count)
                .ThenBy(static item => item.GroupKey, StringComparer.Ordinal),
        ];
    }

    /// <summary>Counts failed, running, and succeeded runs now, each compared with the count an hour ago.</summary>
    public IReadOnlyList<DashboardFigure> GetOverview()
    {
        var now = timeProvider.GetUtcNow();
        var hourAgo = now.AddHours(-1);
        var runs = store.Runs;

        return
        [
            figure("failed", "Failed", DiagnosticsRunStatus.Failed, higherIsBetter: false),
            figure("running", "Running", DiagnosticsRunStatus.Running, higherIsBetter: true),
            figure("succeeded", "Succeeded", DiagnosticsRunStatus.Succeeded, higherIsBetter: true),
        ];

        DashboardFigure figure(string id, string label, DiagnosticsRunStatus status, bool higherIsBetter)
        {
            return new DashboardFigure
            {
                Id = moduleId + ".overview." + id,
                Label = label,
                Value = runs.Count(run => run.Status == status),
                Comparison = new DashboardFigureComparison
                {
                    Label = "an hour ago",
                    Value = runs.Count(run => StatusAt(run, hourAgo) == status),
                    HigherIsBetter = higherIsBetter,
                },
            };
        }
    }

    /// <summary>Returns the run whose id is exactly <paramref name="query"/>.</summary>
    public IReadOnlyList<DashboardObject> Resolve(string query)
    {
        return store.Find(query) is { } run ? [ToObject(run)] : [];
    }

    /// <summary>
    /// Returns the runs linked to <paramref name="target"/>, an object of any module, and for one of this module's
    /// runs, the other runs that carry its trace id.
    /// </summary>
    public IReadOnlyList<DashboardRelatedGroup> GetRelated(ObjectRef target)
    {
        var groups = new List<DashboardRelatedGroup>(2);

        var linked = store.LinkedTo(target);

        if (linked.Count > 0)
        {
            groups.Add(_Group(LinkedRunsGroupId, "Diagnostic runs", linked));
        }

        if (
            string.Equals(target.Module, moduleId, StringComparison.Ordinal)
            && string.Equals(target.Kind, RunKind, StringComparison.Ordinal)
            && store.Find(target.Id) is { } run
        )
        {
            var sameTrace = store
                .Runs.Where(other =>
                    !string.Equals(other.Id, run.Id, StringComparison.Ordinal)
                    && string.Equals(other.TraceId, run.TraceId, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();

            if (sameTrace.Length > 0)
            {
                groups.Add(_Group(SameTraceGroupId, "Runs in the same trace", sameTrace));
            }
        }

        return groups;
    }

    /// <summary>Returns a run's raw payload, with the header the formatter redacts.</summary>
    public DashboardPayload? GetPayload(ObjectRef target)
    {
        if (!_IsOwnRun(target) || store.Find(target.Id) is not { } run)
        {
            return null;
        }

        return new DashboardPayload
        {
            Content = run.Payload,
            ContentType = "application/json",
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["x-api-key"] = "diag-key-" + run.Id,
                ["x-trace-id"] = run.TraceId,
            },
        };
    }

    /// <summary>
    /// Masks every secret-named JSON property, at any depth, and every secret-named header. A body that is not JSON
    /// passes unchanged.
    /// </summary>
    public static DashboardPayload Format(DashboardPayload payload)
    {
        var content = payload.Content;

        if (payload.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var root = JsonNode.Parse(content);

                if (root is not null)
                {
                    _Mask(root);
                    content = root.ToJsonString();
                }
            }
            catch (JsonException)
            {
                // Not JSON after all: nothing to mask by name.
            }
        }

        return payload with
        {
            Content = content,
            Headers = payload.Headers.ToDictionary(
                static header => header.Key,
                static header => _SecretNames.Contains(header.Key) ? _Redacted : header.Value,
                StringComparer.Ordinal
            ),
        };
    }

    public string GetStartCursor()
    {
        return store.Sequence.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the runs changed after <paramref name="cursor"/>, a store sequence.</summary>
    /// <exception cref="ArgumentException"><paramref name="cursor"/> is not a sequence this module returned.</exception>
    public DashboardChangeBatch ReadChanges(string cursor)
    {
        if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var after))
        {
            throw new ArgumentException($"'{cursor}' is not a diagnostic change cursor.", nameof(cursor));
        }

        var runs = store.ReadChanges(after, ChangePageSize);

        return new DashboardChangeBatch
        {
            Changes = [.. runs.Select(run => ToChange(run, affectsAttention: true))],
            Cursor = runs.Count == 0 ? cursor : runs[^1].Version.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The change a live view patches its row with.</summary>
    /// <remarks>
    /// A change read cannot tell whether the run entered or left the failed status since the last read, so every
    /// read change refreshes Needs you; a change published by this node knows and says so.
    /// </remarks>
    public DashboardChange ToChange(DiagnosticsRun run, bool affectsAttention)
    {
        return new DashboardChange
        {
            Target = RefOf(run.Id),
            Version = run.Version.ToString(CultureInfo.InvariantCulture),
            Projection = JsonSerializer.SerializeToElement(
                DiagnosticsRunSummary.From(run),
                DiagnosticsJsonContext.Default.DiagnosticsRunSummary
            ),
            AffectsAttention = affectsAttention,
        };
    }

    /// <summary>The status <paramref name="run"/> had at <paramref name="at"/>, or <see langword="null"/> before it existed.</summary>
    internal static DiagnosticsRunStatus? StatusAt(DiagnosticsRun run, DateTimeOffset at)
    {
        DiagnosticsRunStatus? status = null;

        foreach (var entry in run.Timeline)
        {
            if (entry.At > at)
            {
                break;
            }

            status = entry.Kind switch
            {
                DiagnosticsRunEventKind.Created or DiagnosticsRunEventKind.Requeued => DiagnosticsRunStatus.Queued,
                DiagnosticsRunEventKind.Started => DiagnosticsRunStatus.Running,
                DiagnosticsRunEventKind.Succeeded => DiagnosticsRunStatus.Succeeded,
                DiagnosticsRunEventKind.Failed => DiagnosticsRunStatus.Failed,
                _ => status,
            };
        }

        return status;
    }

    private bool _IsOwnRun(ObjectRef target)
    {
        return string.Equals(target.Module, moduleId, StringComparison.Ordinal)
            && string.Equals(target.Kind, RunKind, StringComparison.Ordinal);
    }

    private DashboardRelatedGroup _Group(string id, string label, IReadOnlyList<DiagnosticsRun> runs)
    {
        return new DashboardRelatedGroup
        {
            Id = id,
            Label = label,
            Items = [.. runs.Take(RelatedPageSize).Select(ToObject)],
            HasMore = runs.Count > RelatedPageSize,
        };
    }

    private static void _Mask(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(static property => property.Key).ToArray())
                {
                    if (_SecretNames.Contains(name))
                    {
                        obj[name] = _Redacted;
                    }
                    else if (obj[name] is { } child)
                    {
                        _Mask(child);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child is not null)
                    {
                        _Mask(child);
                    }
                }

                break;
        }
    }
}
