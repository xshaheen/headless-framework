// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>
/// The Jobs dashboard module: runs, cron jobs, functions, and nodes of the host's Jobs store, with Needs you,
/// Overview figures and strip tracks, id lookup, related runs, payloads, live changes from every node, commands, and
/// Operations.
/// </summary>
/// <remarks>
/// Its object kinds are <c>run</c> (a time job or a cron occurrence), <c>cron-job</c>, <c>function</c>, and
/// <c>node</c>, so its stream topics are <c>jobs.run</c> and <c>jobs.cron-job</c>. Its commands are
/// <c>jobs.requeue</c>, <c>jobs.cancel</c>, and <c>jobs.delete</c> on a run, which also run over a filter as
/// Operations, and <c>jobs.run-now</c>, <c>jobs.pause</c>, <c>jobs.resume</c>, and <c>jobs.edit-cron</c> on a cron job.
/// Its own API is mapped by <see cref="JobsModuleApi"/>.
/// </remarks>
internal sealed class JobsModule
    : IDashboardModule,
        IDashboardAttentionSource,
        IDashboardOverviewSource,
        IDashboardObjectResolver,
        IDashboardRelatedSource,
        IDashboardPayloadSource,
        IDashboardChangeSource,
        IDashboardCommandSource,
        IDashboardOperationRunner
{
    private readonly JobsSources _sources;
    private readonly JobsChangeSource _changes;
    private readonly JobsCommands _commands;
    private readonly JobsModuleApi _api;

    public JobsModule(
        IJobsDashboardBackend backend,
        JobsChangePublisher publisher,
        TimeProvider timeProvider,
        JobsDashboardOptions options
    )
    {
        Argument.IsNotNull(backend);
        Argument.IsNotNull(publisher);
        Argument.IsNotNull(timeProvider);
        Argument.IsNotNull(options);

        _sources = new JobsSources(Id, backend, timeProvider);
        _changes = new JobsChangeSource(Id, backend, timeProvider, options.ChangePageSize, options.ChangeOverlapWindow);
        _commands = new JobsCommands(Id, backend, publisher, timeProvider);
        _api = new JobsModuleApi(backend, timeProvider);
        GroupIds = [_sources.SameCorrelationGroupId, _sources.ChainGroupId, _sources.CronJobGroupId];
    }

    public string Id => JobsDashboardOptions.ModuleId;

    public string DisplayName => "Jobs";

    public DashboardModuleAssets Assets { get; } = JobsAssets.Create();

    public IReadOnlyList<string> GroupIds { get; }

    public IReadOnlyList<DashboardCommand> Commands => _commands.Commands;

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _api.Map(endpoints);
    }

    public Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetAttentionAsync(_commands.RequeueId, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetOverviewAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DashboardScheduleTrack>> GetScheduleAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetScheduleAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _sources.ResolveAsync(request.Query, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardRelatedGroup>> GetRelatedAsync(
        DashboardRelatedRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _sources.GetRelatedAsync(request.Target, cancellationToken);
    }

    public Task<DashboardPayload?> GetPayloadAsync(DashboardPayloadRequest request, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(request);

        return _sources.GetPayloadAsync(request.Target, cancellationToken);
    }

    public Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return _changes.GetStartCursorAsync(cancellationToken);
    }

    public Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(cursor);

        return _changes.ReadChangesAsync(cursor, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.GetAllowedAsync(request.Targets, cancellationToken);
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.ExecuteAsync(request, cancellationToken);
    }

    public Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.PrepareAsync(request, cancellationToken);
    }

    public Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _commands.RunChunkAsync(request, cancellationToken);
    }
}
