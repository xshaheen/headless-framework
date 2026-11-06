// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's read capabilities: Needs you, Overview, id lookups, related runs, and payloads.
/// </summary>
internal sealed class JobsSources(string moduleId, IJobsDashboardBackend backend, TimeProvider timeProvider)
{
    /// <summary>The most runs a related group lists.</summary>
    internal const int RelatedPageSize = 20;

    /// <summary>The most past runs one strip track holds: a cron firing every 30 seconds fills it in the hour.</summary>
    internal const int StripRunsPerTrack = 200;

    /// <summary>The most next fires one cron job adds to its track.</summary>
    internal const int StripFiresPerCronJob = 50;

    /// <summary>How far the strip reaches each side of now: its default window.</summary>
    private static readonly TimeSpan _StripReach = TimeSpan.FromHours(1);

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly IJobsDashboardBackend _backend = Argument.IsNotNull(backend);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    /// <summary>Gets the related group that lists the other runs carrying a run's correlation id.</summary>
    public string SameCorrelationGroupId => _moduleId + ".same-correlation";

    /// <summary>Gets the related group that lists a run's chain parent and children.</summary>
    public string ChainGroupId => _moduleId + ".chain";

    /// <summary>Gets the related group that lists the cron job an occurrence belongs to.</summary>
    public string CronJobGroupId => _moduleId + ".cron-job";

    /// <summary>
    /// Lists one item per failing function and failure fingerprint, recommending Requeue over the group, and one per
    /// registered function that no live node's run filter claims, linking to its Function page.
    /// </summary>
    public async Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        string requeueCommandId,
        CancellationToken cancellationToken
    )
    {
        var hourAgo = _timeProvider.GetUtcNow().AddHours(-1);
        var groups = await _backend.GetFailureGroupsAsync(hourAgo, cancellationToken).ConfigureAwait(false);
        var items = new List<DashboardAttentionItem>(groups.Count);

        foreach (var group in groups)
        {
            var key = group.Function + ":" + (group.FailureFingerprint ?? string.Empty);
            var count = group.Count.ToString(CultureInfo.InvariantCulture);

            items.Add(
                new DashboardAttentionItem
                {
                    Id = "failed:" + key,
                    GroupKey = key,
                    Title =
                        group.Count == 1
                            ? $"1 failed run of {group.Function}"
                            : $"{count} failed runs of {group.Function}",
                    Severity = DashboardAttentionSeverity.Critical,
                    Count = group.Count,
                    Target = JobsViews.Ref(_moduleId, JobsViews.FunctionKind, group.Function),
                    Since = group.FirstFailedAt,
                    LastSeenAt = group.LastFailedAt,
                    RecentCount = group.RecentCount,
                    // A filter cannot select runs without a fingerprint, so such a group gets no Operation that could
                    // reach past it.
                    RecommendedCommandId = group.FailureFingerprint is null ? null : requeueCommandId,
                    GroupFilter = group.FailureFingerprint is null
                        ? null
                        : JsonSerializer.SerializeToElement(
                            new JobsRunScope
                            {
                                Functions = [group.Function],
                                Fingerprint = group.FailureFingerprint,
                                ActionableOnly = true,
                            },
                            JobsDashboardJsonContext.Default.JobsRunScope
                        ),
                }
            );
        }

        var nodes = await _backend.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        var claimed = nodes.SelectMany(static node => node.ClaimedFunctions ?? []).ToHashSet(StringComparer.Ordinal);

        foreach (var function in _backend.Functions)
        {
            if (!claimed.Contains(function.Name))
            {
                items.Add(
                    new DashboardAttentionItem
                    {
                        Id = "unclaimed:" + function.Name,
                        GroupKey = "unclaimed:" + function.Name,
                        Title = $"No live node runs {function.Name}",
                        Severity = DashboardAttentionSeverity.Warning,
                        Count = 1,
                        Target = JobsViews.Ref(_moduleId, JobsViews.FunctionKind, function.Name),
                    }
                );
            }
        }

        return items;
    }

    /// <summary>
    /// The share of runs that succeeded, the failed runs, and the runs that started late, each over the last 24 hours
    /// and compared with the 24 hours before. The share is left out when no run finished in the last 24 hours, so an
    /// idle system does not read as 0 % succeeded, and compares with nothing, saying so, when no run finished in the 24
    /// hours before.
    /// </summary>
    public async Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var figures = await _backend
            .GetOverviewFiguresAsync(_timeProvider.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);
        var (current, previous) = (figures.Current, figures.Previous);
        const string comparison = "previous 24 h";
        var result = new List<DashboardFigure>(3);

        if (current.SucceededShare is { } succeeded)
        {
            result.Add(
                new DashboardFigure
                {
                    Id = _moduleId + ".overview.succeeded-share",
                    Label = "Runs that succeeded, last 24 h",
                    Value = Math.Round(succeeded * 100, 1, MidpointRounding.AwayFromZero),
                    Unit = "%",
                    Comparison = previous.SucceededShare is { } share
                        ? new DashboardFigureComparison
                        {
                            Label = comparison,
                            Value = Math.Round(share * 100, 1, MidpointRounding.AwayFromZero),
                            HigherIsBetter = true,
                        }
                        : new DashboardFigureComparison { Label = "no runs finished in the " + comparison },
                }
            );
        }

        result.Add(
            new DashboardFigure
            {
                Id = _moduleId + ".overview.failed",
                Label = "Failed runs, last 24 h",
                Value = current.Failed,
                Comparison = new DashboardFigureComparison { Label = comparison, Value = previous.Failed },
            }
        );
        result.Add(
            new DashboardFigure
            {
                Id = _moduleId + ".overview.late",
                Label = "Runs that started late, last 24 h",
                Value = current.Late,
                Comparison = new DashboardFigureComparison { Label = comparison, Value = previous.Late },
            }
        );

        return result;
    }

    /// <summary>
    /// One strip track per function that has cron jobs: its cron occurrences due in the last hour, filled in their
    /// status tone, and the next fires of its active cron jobs in the next hour, hollow.
    /// </summary>
    public async Task<IReadOnlyList<DashboardScheduleTrack>> GetScheduleAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cronJobs = await _backend.GetCronJobsAsync(cancellationToken).ConfigureAwait(false);
        var tracks = new List<DashboardScheduleTrack>();

        foreach (
            var function in cronJobs
                .GroupBy(static c => c.Function, StringComparer.Ordinal)
                .OrderBy(static g => g.Key, StringComparer.Ordinal)
        )
        {
            var page = await _backend
                .ListRunsAsync(
                    new JobRunQuery
                    {
                        Kind = JobRunKind.CronJobOccurrence,
                        Functions = [function.Key],
                        ExecutionFrom = now - _StripReach,
                        ExecutionBefore = now,
                    },
                    after: null,
                    StripRunsPerTrack,
                    cancellationToken
                )
                .ConfigureAwait(false);
            var ticks = new List<DashboardScheduleTick>(page.Runs.Count);

            foreach (var run in page.Runs)
            {
                ticks.Add(
                    new DashboardScheduleTick
                    {
                        Id = run.Id.ToString(),
                        At = new DateTimeOffset(run.ExecutionTime ?? run.CreatedAt.UtcDateTime, TimeSpan.Zero),
                        Label = JobsViews.StatusWords(run.Status),
                        Tone = JobsViews.StatusTone(run.Status),
                        Target = JobsViews.Ref(_moduleId, JobsViews.RunKind, run.Id.ToString()),
                    }
                );
            }

            foreach (var cronJob in function.Where(static c => !c.IsPaused))
            {
                var preview = _backend.Preview(cronJob.Expression, cronJob.TimeZoneId, now, StripFiresPerCronJob);

                foreach (var fire in preview.NextFiresUtc.Where(fire => fire <= now + _StripReach))
                {
                    ticks.Add(
                        new DashboardScheduleTick
                        {
                            Id = $"fire:{cronJob.Id}:{fire.Ticks.ToString(CultureInfo.InvariantCulture)}",
                            At = new DateTimeOffset(fire, TimeSpan.Zero),
                            Label = "next fire",
                            Target = JobsViews.Ref(_moduleId, JobsViews.CronJobKind, cronJob.Id.ToString()),
                        }
                    );
                }
            }

            tracks.Add(
                new DashboardScheduleTrack
                {
                    Id = function.Key,
                    Label = function.Key,
                    Ticks = ticks,
                }
            );
        }

        return tracks;
    }

    /// <summary>Returns the run or cron job whose id is exactly <paramref name="query"/>, or the function so named.</summary>
    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(string query, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(query, out var id))
        {
            if (await _backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is { } run)
            {
                return [JobsViews.ToObject(_moduleId, run)];
            }

            if (await _backend.GetCronJobAsync(id, cancellationToken).ConfigureAwait(false) is { } cronJob)
            {
                return [JobsViews.ToObject(_moduleId, cronJob)];
            }

            return [];
        }

        return _backend.Functions.Any(function => string.Equals(function.Name, query, StringComparison.Ordinal))
            ? [new DashboardObject { Target = JobsViews.Ref(_moduleId, JobsViews.FunctionKind, query), Title = query }]
            : [];
    }

    /// <summary>
    /// For one of this module's runs: the other runs with its correlation id, its chain parent and children, and the
    /// cron job of an occurrence.
    /// </summary>
    public async Task<IReadOnlyList<DashboardRelatedGroup>> GetRelatedAsync(
        ObjectRef target,
        CancellationToken cancellationToken
    )
    {
        if (
            !string.Equals(target.Module, _moduleId, StringComparison.Ordinal)
            || !string.Equals(target.Kind, JobsViews.RunKind, StringComparison.Ordinal)
            || !Guid.TryParse(target.Id, out var id)
            || await _backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is not { } run
        )
        {
            return [];
        }

        var groups = new List<DashboardRelatedGroup>(3);

        if (run.CorrelationId is { Length: > 0 } correlationId)
        {
            var page = await _backend
                .ListRunsAsync(
                    new JobRunQuery { CorrelationId = correlationId, NewestFirst = true },
                    after: null,
                    RelatedPageSize + 2,
                    cancellationToken
                )
                .ConfigureAwait(false);
            var others = page.Runs.Where(other => other.Id != run.Id).ToArray();

            if (others.Length > 0)
            {
                groups.Add(
                    new DashboardRelatedGroup
                    {
                        Id = SameCorrelationGroupId,
                        Label = "Same correlation",
                        Items = [.. others.Take(RelatedPageSize).Select(other => JobsViews.ToObject(_moduleId, other))],
                        HasMore = others.Length > RelatedPageSize || page.Next is not null,
                    }
                );
            }
        }

        var chain = new List<DashboardObject>();

        if (
            run.ParentId is { } parentId
            && await _backend.GetRunAsync(parentId, cancellationToken).ConfigureAwait(false) is { } parent
        )
        {
            chain.Add(JobsViews.ToObject(_moduleId, parent));
        }

        if (run.Kind == JobRunKind.TimeJob)
        {
            foreach (var childId in await _backend.GetChildIdsAsync(run.Id, cancellationToken).ConfigureAwait(false))
            {
                if (await _backend.GetRunAsync(childId, cancellationToken).ConfigureAwait(false) is { } child)
                {
                    chain.Add(JobsViews.ToObject(_moduleId, child));
                }
            }
        }

        if (chain.Count > 0)
        {
            groups.Add(
                new DashboardRelatedGroup
                {
                    Id = ChainGroupId,
                    Label = "Chain",
                    Items = chain,
                }
            );
        }

        if (
            run.CronJobId is { } cronJobId
            && await _backend.GetCronJobAsync(cronJobId, cancellationToken).ConfigureAwait(false) is { } cronJob
        )
        {
            groups.Add(
                new DashboardRelatedGroup
                {
                    Id = CronJobGroupId,
                    Label = "Cron job",
                    Items = [JobsViews.ToObject(_moduleId, cronJob)],
                }
            );
        }

        return groups;
    }

    /// <summary>Returns a run's stored request as JSON, or the reason the host cannot read its contract.</summary>
    public async Task<DashboardPayload?> GetPayloadAsync(ObjectRef target, CancellationToken cancellationToken)
    {
        if (
            !string.Equals(target.Module, _moduleId, StringComparison.Ordinal)
            || !string.Equals(target.Kind, JobsViews.RunKind, StringComparison.Ordinal)
            || !Guid.TryParse(target.Id, out var id)
            || await _backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is not { } run
            || await _backend.GetRequestAsync(run.Kind, run.Id, cancellationToken).ConfigureAwait(false)
                is not { } request
        )
        {
            return null;
        }

        return request.Unsupported is { } unsupported
            ? new DashboardPayload { Content = unsupported, ContentType = "text/plain" }
            : new DashboardPayload { Content = request.Json, ContentType = "application/json" };
    }
}
