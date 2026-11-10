// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's commands: Requeue, Cancel, and Delete on a run, which also run over a filter as Operations; Edit,
/// Duplicate, and Force delete on a time job; and Run now, Pause, Resume, Edit schedule, and Delete on a cron job.
/// Every answer comes from <see cref="JobsRules"/>.
/// </summary>
/// <remarks>
/// The shell enforces each command's permission, confirmation, reason, and argument schema before this class runs, so
/// this class re-checks the rules against the stored row, applies the command, and publishes the rows it changed to
/// this node's streams. A call on one object that the rules refuse changes nothing and answers with the refusal, which
/// the shell returns as <c>409</c>.
/// </remarks>
internal sealed class JobsCommands(
    string moduleId,
    IJobsDashboardBackend backend,
    JobsChangePublisher publisher,
    TimeProvider timeProvider
)
{
    public const string ExpressionArgument = "expression";
    public const string TimeZoneArgument = "timeZoneId";
    public const string ExecutionTimeArgument = "executionTime";
    public const string RequestArgument = "request";
    public const string RetriesArgument = "retries";
    public const string RetryIntervalsArgument = "retryIntervals";

    private const string _MarkSeparator = ":";
    private const int _MaxRequestLength = 65_536;
    private const int _MaxRetries = 100;

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly IJobsDashboardBackend _backend = Argument.IsNotNull(backend);
    private readonly JobsChangePublisher _publisher = Argument.IsNotNull(publisher);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public string RequeueId => _moduleId + ".requeue";

    public string CancelId => _moduleId + ".cancel";

    public string DeleteId => _moduleId + ".delete";

    public string RunNowId => _moduleId + ".run-now";

    public string PauseId => _moduleId + ".pause";

    public string ResumeId => _moduleId + ".resume";

    public string EditCronId => _moduleId + ".edit-cron";

    public string EditId => _moduleId + ".edit";

    public string DuplicateId => _moduleId + ".duplicate";

    public string ForceDeleteId => _moduleId + ".force-delete";

    public string DeleteCronId => _moduleId + ".delete-cron";

    public IReadOnlyList<DashboardCommand> Commands =>
        field ??= [
            new()
            {
                Id = RequeueId,
                Label = "Requeue",
                ObjectKind = JobsViews.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
            },
            new()
            {
                Id = CancelId,
                Label = "Cancel",
                ObjectKind = JobsViews.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
            },
            new()
            {
                Id = DeleteId,
                Label = "Delete",
                ObjectKind = JobsViews.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
            },
            new()
            {
                Id = RunNowId,
                Label = "Run now",
                ObjectKind = JobsViews.CronJobKind,
            },
            new()
            {
                Id = PauseId,
                Label = "Pause",
                ObjectKind = JobsViews.CronJobKind,
                InverseCommandId = ResumeId,
            },
            new()
            {
                Id = ResumeId,
                Label = "Resume",
                ObjectKind = JobsViews.CronJobKind,
                InverseCommandId = PauseId,
            },
            new()
            {
                Id = EditCronId,
                Label = "Edit schedule",
                ObjectKind = JobsViews.CronJobKind,
                Arguments =
                [
                    new()
                    {
                        Name = ExpressionArgument,
                        Label = "Expression",
                        Kind = DashboardArgumentKind.Text,
                        Required = true,
                        MaxLength = 200,
                    },
                    new()
                    {
                        Name = TimeZoneArgument,
                        Label = "Time zone",
                        Kind = DashboardArgumentKind.Text,
                        MaxLength = 100,
                    },
                ],
            },
            // Every argument is optional: one the operator leaves out keeps its stored value. The due time has no
            // floor, because a form filled from a job already due would refuse its own unchanged value.
            new()
            {
                Id = EditId,
                Label = "Edit",
                ObjectKind = JobsViews.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
                Arguments =
                [
                    new()
                    {
                        Name = ExecutionTimeArgument,
                        Label = "Execution time",
                        Kind = DashboardArgumentKind.DateTime,
                    },
                    new()
                    {
                        Name = RequestArgument,
                        Label = "Request",
                        Kind = DashboardArgumentKind.Json,
                        MaxLength = _MaxRequestLength,
                    },
                    new()
                    {
                        Name = RetriesArgument,
                        Label = "Retries",
                        Kind = DashboardArgumentKind.WholeNumber,
                        Minimum = 0,
                        Maximum = _MaxRetries,
                    },
                    new()
                    {
                        Name = RetryIntervalsArgument,
                        Label = "Retry intervals (seconds)",
                        Kind = DashboardArgumentKind.Json,
                        MaxLength = 2000,
                    },
                ],
            },
            // The server copies the request, so the operator only chooses when the copy runs; none means now.
            new()
            {
                Id = DuplicateId,
                Label = "Duplicate",
                ObjectKind = JobsViews.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
                Arguments =
                [
                    new()
                    {
                        Name = ExecutionTimeArgument,
                        Label = "Execution time",
                        Kind = DashboardArgumentKind.DateTime,
                        NotBeforeNow = true,
                    },
                ],
            },
            new()
            {
                Id = ForceDeleteId,
                Label = "Force delete",
                ObjectKind = JobsViews.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
            },
            new()
            {
                Id = DeleteCronId,
                Label = "Delete",
                ObjectKind = JobsViews.CronJobKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
            },
        ];

    public async Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedAsync(
        IReadOnlyList<ObjectRef> targets,
        CancellationToken cancellationToken
    )
    {
        var answers = new List<DashboardCommandAvailability>(targets.Count * 6);

        foreach (var target in targets)
        {
            if (!string.Equals(target.Module, _moduleId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(target.Kind, JobsViews.RunKind, StringComparison.Ordinal))
            {
                var refusals = await _RunRefusalsAsync(target.Id, cancellationToken).ConfigureAwait(false);
                answers.Add(_Answer(target, RequeueId, refusals.Requeue));
                answers.Add(_Answer(target, CancelId, refusals.Cancel));
                answers.Add(_Answer(target, DeleteId, refusals.Delete));
                answers.Add(_Answer(target, EditId, refusals.Edit));
                answers.Add(_Answer(target, DuplicateId, refusals.Duplicate));
                answers.Add(_Answer(target, ForceDeleteId, refusals.ForceDelete));
            }
            else if (string.Equals(target.Kind, JobsViews.CronJobKind, StringComparison.Ordinal))
            {
                var (runNow, pause, resume, edit, delete) = await _CronRefusalsAsync(target.Id, cancellationToken)
                    .ConfigureAwait(false);
                answers.Add(_Answer(target, RunNowId, runNow));
                answers.Add(_Answer(target, PauseId, pause));
                answers.Add(_Answer(target, ResumeId, resume));
                answers.Add(_Answer(target, EditCronId, edit));
                answers.Add(_Answer(target, DeleteCronId, delete));
            }
        }

        return answers;
    }

    public async Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<DashboardRowResult>(request.Ids.Count);

        foreach (var id in request.Ids)
        {
            var (row, refused) = await _ApplyAsync(request, id, cancellationToken).ConfigureAwait(false);

            // A call on one object the rules refuse answers as a refusal: nothing changed, and the caller learns why.
            if (refused && request.Ids.Count == 1)
            {
                return new DashboardCommandResult { Rows = [row], Refusal = row.Reason };
            }

            rows.Add(row);
        }

        return new DashboardCommandResult { Rows = rows };
    }

    /// <summary>
    /// Freezes a Requeue, Cancel, or Delete over a filter: the exact count up to one past the limit, a sample, and a
    /// mark carrying the store's latest change marker and the confirm time.
    /// </summary>
    /// <exception cref="ArgumentException">The command does not run over a filter, or the filter is malformed.</exception>
    public async Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        var kind = _OperationKind(request.CommandId);
        var highWater = await _backend.GetLatestChangeMarkerAsync(cancellationToken).ConfigureAwait(false);
        var scope = new JobOperationScope
        {
            Kind = kind,
            Query = _ParseFilter(request.Filter).ToQuery(),
            HighWaterMark = DateTime.SpecifyKind(highWater ?? DateTime.MinValue, DateTimeKind.Utc),
            ConfirmedAt = _timeProvider.GetUtcNow(),
        };

        var count = await _backend
            .CountOperationScopeAsync(scope, request.CountLimit, cancellationToken)
            .ConfigureAwait(false);
        var first = await _backend
            .ReadOperationScopeAsync(scope, after: null, Math.Max(1, request.SampleSize), cancellationToken)
            .ConfigureAwait(false);
        var sample = new List<DashboardObject>(first.Targets.Count);

        foreach (var target in first.Targets)
        {
            if (await _backend.GetRunAsync(target.Id, cancellationToken).ConfigureAwait(false) is { } run)
            {
                sample.Add(JobsViews.ToObject(_moduleId, run));
            }
        }

        return new DashboardOperationScope
        {
            Count = count.Capped ? (long)request.CountLimit + 1 : count.Count,
            Sample = sample,
            HighWaterMark = _FormatMark(scope),
        };
    }

    /// <summary>
    /// Runs the next chunk of a frozen scope. Requeue and Delete write each row only while its change marker still
    /// equals the one the scope read returned, so a row an earlier chunk, a concurrent chunk on another node, or an
    /// earlier run of the Operation changed is skipped. Cancel applies its own rule per row, because a running row
    /// advances its marker as it reports progress, and a repeated cancel request changes nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The mark or the cursor is not one this module returned.</exception>
    public async Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        var kind = _OperationKind(request.CommandId);
        var (highWater, confirmedAt) = _ParseMark(request.HighWaterMark);
        JobOperationCursor? cursor = null;

        if (request.Cursor is { } value)
        {
            cursor = JobOperationCursor.TryParse(value, out var parsed)
                ? parsed
                : throw new ArgumentException($"'{value}' is not a Jobs Operation cursor.", nameof(request));
        }

        var scope = new JobOperationScope
        {
            Kind = kind,
            Query = _ParseFilter(request.Filter).ToQuery(),
            HighWaterMark = highWater,
            ConfirmedAt = confirmedAt,
        };

        var chunk = await _backend
            .ReadOperationScopeAsync(scope, cursor, request.ChunkSize, cancellationToken)
            .ConfigureAwait(false);
        var rows = new List<DashboardRowResult>(chunk.Targets.Count);

        foreach (var target in chunk.Targets)
        {
            rows.Add(await _ApplyOperationAsync(kind, target, cancellationToken).ConfigureAwait(false));
        }

        return new DashboardOperationChunk { Rows = rows, NextCursor = chunk.Next?.ToString() };
    }

    private async Task<DashboardRowResult> _ApplyOperationAsync(
        JobOperationKind kind,
        JobOperationTarget target,
        CancellationToken cancellationToken
    )
    {
        var id = target.Id.ToString();
        var changeKind = _ChangeKind(target.Kind);

        switch (kind)
        {
            case JobOperationKind.Requeue:
            {
                var outcome = await _backend
                    .RequeueAsync(target.Kind, target.Id, target.ChangedAt, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome != JobRequeueOutcome.Requeued)
                {
                    return _Skipped(id, _RequeueReason(outcome));
                }

                _publisher.Enqueue(changeKind, target.Id, affectsAttention: true);

                return _Done(id);
            }
            case JobOperationKind.Cancel:
            {
                if (!await _backend.CancelAsync(target.Id, cancellationToken).ConfigureAwait(false))
                {
                    return _Skipped(id, JobsRules.Finished);
                }

                _publisher.Enqueue(changeKind, target.Id, affectsAttention: true);

                return _Done(id);
            }
            default:
            {
                if (
                    !await _backend
                        .DeleteAsync(target.Kind, target.Id, target.ChangedAt, cancellationToken)
                        .ConfigureAwait(false)
                )
                {
                    return _Skipped(id, "Changed since the Operation was confirmed");
                }

                _publisher.PublishRemoved(changeKind, target.Id);

                return _Done(id);
            }
        }
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyAsync(
        DashboardCommandRequest request,
        string id,
        CancellationToken cancellationToken
    )
    {
        var command = request.CommandId;

        if (
            string.Equals(command, RequeueId, StringComparison.Ordinal)
            || string.Equals(command, CancelId, StringComparison.Ordinal)
            || string.Equals(command, DeleteId, StringComparison.Ordinal)
        )
        {
            return await _ApplyToRunAsync(command, id, cancellationToken).ConfigureAwait(false);
        }

        if (
            string.Equals(command, EditId, StringComparison.Ordinal)
            || string.Equals(command, DuplicateId, StringComparison.Ordinal)
            || string.Equals(command, ForceDeleteId, StringComparison.Ordinal)
        )
        {
            return await _ApplyToTimeJobAsync(request, id, cancellationToken).ConfigureAwait(false);
        }

        return await _ApplyToCronJobAsync(request, id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToRunAsync(
        string command,
        string id,
        CancellationToken cancellationToken
    )
    {
        var run = Guid.TryParse(id, out var guid)
            ? await _backend.GetRunAsync(guid, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is null)
        {
            return (_Skipped(id, JobsRules.RunGone), true);
        }

        var changeKind = _ChangeKind(run.Kind);

        if (string.Equals(command, RequeueId, StringComparison.Ordinal))
        {
            if (
                JobsRules.Requeue(run, await _IsChainMemberAsync(run, cancellationToken).ConfigureAwait(false)) is
                { } refusal
            )
            {
                return (_Skipped(id, refusal), true);
            }

            var outcome = await _backend
                .RequeueAsync(run.Kind, run.Id, expectedChangedAt: null, cancellationToken)
                .ConfigureAwait(false);

            if (outcome != JobRequeueOutcome.Requeued)
            {
                return (_Skipped(id, _RequeueReason(outcome)), true);
            }
        }
        else if (string.Equals(command, CancelId, StringComparison.Ordinal))
        {
            if (JobsRules.Cancel(run) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            if (!await _backend.CancelAsync(run.Id, cancellationToken).ConfigureAwait(false))
            {
                return (_Skipped(id, JobsRules.Finished), true);
            }
        }
        else
        {
            if (JobsRules.Delete(run) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            if (!await _backend.DeleteAsync(run.Kind, run.Id, run.ChangedAt, cancellationToken).ConfigureAwait(false))
            {
                return (_Skipped(id, "Changed while deleting — try again"), true);
            }

            _publisher.PublishRemoved(changeKind, run.Id);

            return (_Done(id), false);
        }

        await _publisher
            .TryPublishAsync(changeKind, run.Id, affectsAttention: true, cancellationToken)
            .ConfigureAwait(false);

        return (_Done(id), false);
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToCronJobAsync(
        DashboardCommandRequest request,
        string id,
        CancellationToken cancellationToken
    )
    {
        var command = request.CommandId;
        var cronJob = Guid.TryParse(id, out var guid)
            ? await _backend.GetCronJobAsync(guid, cancellationToken).ConfigureAwait(false)
            : null;

        if (cronJob is null)
        {
            return (_Skipped(id, JobsRules.CronJobGone), true);
        }

        string? done = null;

        if (string.Equals(command, RunNowId, StringComparison.Ordinal))
        {
            var running = await _HasRunningOccurrenceAsync(cronJob, cancellationToken).ConfigureAwait(false);

            if (JobsRules.RunNow(cronJob, running) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            var occurrence = await _backend.RunCronJobNowAsync(cronJob.Id, cancellationToken).ConfigureAwait(false);
            await _publisher
                .TryPublishAsync(
                    JobChangeKind.CronJobOccurrence,
                    occurrence,
                    affectsAttention: false,
                    cancellationToken
                )
                .ConfigureAwait(false);

            done = occurrence.ToString();
        }
        else if (string.Equals(command, PauseId, StringComparison.Ordinal))
        {
            if (JobsRules.Pause(cronJob) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            if (!await _backend.PauseCronJobAsync(cronJob.Id, cancellationToken).ConfigureAwait(false))
            {
                return (_Skipped(id, JobsRules.AlreadyPaused), true);
            }
        }
        else if (string.Equals(command, ResumeId, StringComparison.Ordinal))
        {
            if (JobsRules.Resume(cronJob) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            if (!await _backend.ResumeCronJobAsync(cronJob.Id, cancellationToken).ConfigureAwait(false))
            {
                return (_Skipped(id, JobsRules.NotPaused), true);
            }
        }
        else if (string.Equals(command, EditCronId, StringComparison.Ordinal))
        {
            if (JobsRules.EditCron(cronJob) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            var expression = _Text(request.Arguments, ExpressionArgument) ?? string.Empty;
            var zone = _Text(request.Arguments, TimeZoneArgument);
            var preview = _backend.Preview(expression, zone, _timeProvider.GetUtcNow().UtcDateTime, count: 1);

            if (!preview.IsValid)
            {
                return (_Failed(id, preview.ValidationMessage!), true);
            }

            if (
                await _backend
                    .UpdateCronScheduleAsync(cronJob.Id, expression, zone, cancellationToken)
                    .ConfigureAwait(false) is
                { } error
            )
            {
                return (_Failed(id, error), true);
            }
        }
        else if (string.Equals(command, DeleteCronId, StringComparison.Ordinal))
        {
            return await _DeleteCronJobAsync(cronJob, id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return (_Failed(id, $"Unknown command '{command}'"), true);
        }

        await _publisher
            .TryPublishAsync(JobChangeKind.CronJob, cronJob.Id, affectsAttention: true, cancellationToken)
            .ConfigureAwait(false);

        return (
            new DashboardRowResult
            {
                Id = id,
                Outcome = DashboardRowOutcome.Done,
                Reason = done,
            },
            false
        );
    }

    private async Task<RunRefusals> _RunRefusalsAsync(string id, CancellationToken cancellationToken)
    {
        var run = Guid.TryParse(id, out var guid)
            ? await _backend.GetRunAsync(guid, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is null)
        {
            return new RunRefusals(
                JobsRules.RunGone,
                JobsRules.RunGone,
                JobsRules.RunGone,
                JobsRules.RunGone,
                JobsRules.RunGone,
                JobsRules.RunGone
            );
        }

        var isChainMember = await _IsChainMemberAsync(run, cancellationToken).ConfigureAwait(false);
        var inChain = await _IsInChainAsync(run, cancellationToken).ConfigureAwait(false);

        return new RunRefusals(
            JobsRules.Requeue(run, isChainMember),
            JobsRules.Cancel(run),
            JobsRules.Delete(run),
            JobsRules.Edit(run),
            _DuplicateRefusal(run, inChain),
            JobsRules.ForceDelete(run, inChain)
        );
    }

    private async Task<(
        string? RunNow,
        string? Pause,
        string? Resume,
        string? Edit,
        string? Delete
    )> _CronRefusalsAsync(string id, CancellationToken cancellationToken)
    {
        var cronJob = Guid.TryParse(id, out var guid)
            ? await _backend.GetCronJobAsync(guid, cancellationToken).ConfigureAwait(false)
            : null;

        if (cronJob is null)
        {
            return (
                JobsRules.CronJobGone,
                JobsRules.CronJobGone,
                JobsRules.CronJobGone,
                JobsRules.CronJobGone,
                JobsRules.CronJobGone
            );
        }

        var running = await _HasRunningOccurrenceAsync(cronJob, cancellationToken).ConfigureAwait(false);
        var inProgress = await _HasInProgressOccurrenceAsync(cronJob.Id, cancellationToken).ConfigureAwait(false);

        return (
            JobsRules.RunNow(cronJob, running),
            JobsRules.Pause(cronJob),
            JobsRules.Resume(cronJob),
            JobsRules.EditCron(cronJob),
            JobsRules.DeleteCron(cronJob, inProgress)
        );
    }

    // Only a requeueable time job needs the chain read, which is the one extra lookup the rule set makes.
    private async Task<bool> _IsChainMemberAsync(JobRunSummary run, CancellationToken cancellationToken)
    {
        if (run.Kind != JobRunKind.TimeJob || !JobRequeueRules.IsRequeueableStatus(run.Status))
        {
            return false;
        }

        return run.ParentId is not null
            || (await _backend.GetChildIdsAsync(run.Id, cancellationToken).ConfigureAwait(false)).Count != 0;
    }

    // The next scheduled occurrence waits Idle on every active cron job, so only a claimed or running one counts.
    private async Task<bool> _HasRunningOccurrenceAsync(CronJobEntity cronJob, CancellationToken cancellationToken)
    {
        if (!CronOverlapRule.ForbidsOverlap(cronJob.OnOverlap))
        {
            return false;
        }

        var running = await _backend
            .CountRunsAsync(
                new JobRunQuery
                {
                    Kind = JobRunKind.CronJobOccurrence,
                    CronJobId = cronJob.Id,
                    Statuses = [JobStatus.Queued, JobStatus.InProgress],
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return running.Count > 0;
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _ApplyToTimeJobAsync(
        DashboardCommandRequest request,
        string id,
        CancellationToken cancellationToken
    )
    {
        var command = request.CommandId;
        var run = Guid.TryParse(id, out var guid)
            ? await _backend.GetRunAsync(guid, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is null)
        {
            return (_Skipped(id, JobsRules.RunGone), true);
        }

        if (string.Equals(command, EditId, StringComparison.Ordinal))
        {
            if (JobsRules.Edit(run) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            if (!_TryReadEdit(request.Arguments, out var edit, out var invalid))
            {
                return (_Failed(id, invalid), true);
            }

            if (await _backend.EditTimeJobAsync(run.Id, edit, cancellationToken).ConfigureAwait(false) is { } error)
            {
                return (_Failed(id, error), true);
            }

            await _publisher
                .TryPublishAsync(JobChangeKind.TimeJob, run.Id, affectsAttention: false, cancellationToken)
                .ConfigureAwait(false);

            return (_Done(id), false);
        }

        var inChain = await _IsInChainAsync(run, cancellationToken).ConfigureAwait(false);

        if (string.Equals(command, DuplicateId, StringComparison.Ordinal))
        {
            if (_DuplicateRefusal(run, inChain) is { } refusal)
            {
                return (_Skipped(id, refusal), true);
            }

            Guid? created;

            try
            {
                created = await _backend
                    .DuplicateTimeJobAsync(
                        run.Id,
                        _Instant(request.Arguments, ExecutionTimeArgument),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException or JobValidatorException)
            {
                return (_Failed(id, exception.Message), true);
            }

            if (created is not { } copy)
            {
                return (_Skipped(id, JobsRules.RunGone), true);
            }

            await _publisher
                .TryPublishAsync(JobChangeKind.TimeJob, copy, affectsAttention: false, cancellationToken)
                .ConfigureAwait(false);

            return (
                new DashboardRowResult
                {
                    Id = id,
                    Outcome = DashboardRowOutcome.Done,
                    Created = new ObjectRef
                    {
                        Module = _moduleId,
                        Kind = JobsViews.RunKind,
                        Id = copy.ToString(),
                    },
                },
                false
            );
        }

        if (JobsRules.ForceDelete(run, inChain) is { } forceRefusal)
        {
            return (_Skipped(id, forceRefusal), true);
        }

        if (!await _backend.ForceDeleteTimeJobAsync(run.Id, cancellationToken).ConfigureAwait(false))
        {
            // The store refused because the run moved since it was read: answer with what it is now.
            var now = await _backend.GetRunAsync(run.Id, cancellationToken).ConfigureAwait(false);
            var reason = now is null
                ? JobsRules.RunGone
                : JobsRules.ForceDelete(now, isChainMember: false) ?? "Changed while deleting — try again";

            return (_Skipped(id, reason), true);
        }

        _publisher.PublishRemoved(JobChangeKind.TimeJob, run.Id);

        return (_Done(id), false);
    }

    private async Task<(DashboardRowResult Row, bool Refused)> _DeleteCronJobAsync(
        CronJobEntity cronJob,
        string id,
        CancellationToken cancellationToken
    )
    {
        var inProgress = await _HasInProgressOccurrenceAsync(cronJob.Id, cancellationToken).ConfigureAwait(false);

        if (JobsRules.DeleteCron(cronJob, inProgress) is { } refusal)
        {
            return (_Skipped(id, refusal), true);
        }

        if (await _backend.DeleteCronJobAsync(cronJob.Id, cancellationToken).ConfigureAwait(false) is not { } removed)
        {
            // The store refused because an occurrence started since the check, or the job went meanwhile.
            var reason = await _backend.GetCronJobAsync(cronJob.Id, cancellationToken).ConfigureAwait(false) is null
                ? JobsRules.CronJobGone
                : JobsRules.RunningCronDelete;

            return (_Skipped(id, reason), true);
        }

        _publisher.PublishRemoved(JobChangeKind.CronJob, cronJob.Id);

        return (
            new DashboardRowResult
            {
                Id = id,
                Outcome = DashboardRowOutcome.Done,
                Reason =
                    removed == 1
                        ? "1 stored occurrence removed"
                        : removed.ToString(CultureInfo.InvariantCulture) + " stored occurrences removed",
            },
            false
        );
    }

    // Unlike the requeue read, membership here does not depend on the run's status: a finished or running step is
    // still part of its chain.
    private async Task<bool> _IsInChainAsync(JobRunSummary run, CancellationToken cancellationToken)
    {
        if (run.Kind != JobRunKind.TimeJob)
        {
            return false;
        }

        return run.ParentId is not null
            || (await _backend.GetChildIdsAsync(run.Id, cancellationToken).ConfigureAwait(false)).Count != 0;
    }

    // Unlike the overlap read, only a started occurrence holds a delete: a claimed one has not run any code yet.
    private async Task<bool> _HasInProgressOccurrenceAsync(Guid cronJobId, CancellationToken cancellationToken)
    {
        var running = await _backend
            .CountRunsAsync(
                new JobRunQuery
                {
                    Kind = JobRunKind.CronJobOccurrence,
                    CronJobId = cronJobId,
                    Statuses = [JobStatus.InProgress],
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return running.Count > 0;
    }

    // A copy runs on this host, so a function it no longer registers, or a request stored under another contract,
    // has nothing to run it.
    private string? _DuplicateRefusal(JobRunSummary run, bool inChain)
    {
        if (JobsRules.Duplicate(run, inChain) is { } refusal)
        {
            return refusal;
        }

        var function = _backend.Functions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, run.Function, StringComparison.Ordinal)
        );

        if (function is null)
        {
            return JobsRules.FunctionNotRegistered;
        }

        return
            run.ContractVersion is { } version
            && !string.Equals(version, function.ContractVersion, StringComparison.Ordinal)
            ? JobsRules.UnsupportedContract
            : null;
    }

    private static bool _TryReadEdit(
        IReadOnlyDictionary<string, JsonElement> arguments,
        out JobsDashboardTimeJobEdit edit,
        out string invalid
    )
    {
        invalid = string.Empty;
        int[]? intervals = null;

        if (_Text(arguments, RetryIntervalsArgument) is { } text && !_TryReadIntervals(text, out intervals))
        {
            edit = new JobsDashboardTimeJobEdit();
            invalid = "Retry intervals must be a JSON array of whole seconds, such as [30, 60, 300]";

            return false;
        }

        var request = _Text(arguments, RequestArgument);

        edit = new JobsDashboardTimeJobEdit
        {
            ExecutionTime = _Instant(arguments, ExecutionTimeArgument),
            ReplacesRequest = request is not null,
            RequestJson = request,
            Retries =
                arguments.TryGetValue(RetriesArgument, out var retries) && retries.ValueKind == JsonValueKind.Number
                    ? retries.GetInt32()
                    : null,
            RetryIntervals = intervals,
        };

        return true;
    }

    // The shell checked the text is JSON, not that it is a list of non-negative whole numbers.
    private static bool _TryReadIntervals(string text, out int[]? intervals)
    {
        intervals = null;

        try
        {
            using var document = JsonDocument.Parse(text);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var values = new List<int>(document.RootElement.GetArrayLength());

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var seconds) || seconds < 0)
                {
                    return false;
                }

                values.Add(seconds);
            }

            intervals = [.. values];

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The shell accepted only ISO 8601 text with an offset, so the instant is unambiguous.
    private static DateTime? _Instant(IReadOnlyDictionary<string, JsonElement> arguments, string name)
    {
        return arguments.TryGetValue(name, out var value) && value.TryGetDateTimeOffset(out var instant)
            ? instant.UtcDateTime
            : null;
    }

    private JobOperationKind _OperationKind(string commandId)
    {
        if (string.Equals(commandId, RequeueId, StringComparison.Ordinal))
        {
            return JobOperationKind.Requeue;
        }

        if (string.Equals(commandId, CancelId, StringComparison.Ordinal))
        {
            return JobOperationKind.Cancel;
        }

        return string.Equals(commandId, DeleteId, StringComparison.Ordinal)
            ? JobOperationKind.Delete
            : throw new ArgumentException($"Command '{commandId}' does not run over a filter.", nameof(commandId));
    }

    private static JobsRunScope _ParseFilter(JsonElement filter)
    {
        try
        {
            return filter.Deserialize(JobsDashboardJsonContext.Default.JobsRunScope) ?? new JobsRunScope();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The filter is not a Jobs run filter.", nameof(filter), exception);
        }
    }

    // The mark carries both freezes: Requeue and Delete stop at the marker, Cancel at the confirm time.
    private static string _FormatMark(JobOperationScope scope)
    {
        return scope.HighWaterMark.Ticks.ToString(CultureInfo.InvariantCulture)
            + _MarkSeparator
            + scope.ConfirmedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
    }

    private static (DateTime HighWater, DateTimeOffset ConfirmedAt) _ParseMark(string mark)
    {
        var parts = mark.Split(_MarkSeparator);

        if (
            parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var highWater)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var confirmedAt)
            && highWater <= DateTime.MaxValue.Ticks
            && confirmedAt <= DateTime.MaxValue.Ticks
        )
        {
            return (new DateTime(highWater, DateTimeKind.Utc), new DateTimeOffset(confirmedAt, TimeSpan.Zero));
        }

        throw new ArgumentException($"'{mark}' is not a Jobs Operation mark.", nameof(mark));
    }

    private static string _RequeueReason(JobRequeueOutcome outcome) =>
        outcome switch
        {
            JobRequeueOutcome.NotFound => JobsRules.RunGone,
            JobRequeueOutcome.ChainMember => JobsRules.ChainMember,
            JobRequeueOutcome.SupersededGeneration => JobsRules.SupersededGeneration,
            JobRequeueOutcome.Overlap => JobsRules.OverlapForbidden,
            JobRequeueOutcome.Conflict => "Changed since it was read — not requeued again",
            _ => JobsRules.NotRequeueable,
        };

    private static JobChangeKind _ChangeKind(JobRunKind kind) =>
        kind == JobRunKind.TimeJob ? JobChangeKind.TimeJob : JobChangeKind.CronJobOccurrence;

    private static string? _Text(IReadOnlyDictionary<string, JsonElement> arguments, string name) =>
        arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DashboardRowResult _Done(string id) => new() { Id = id, Outcome = DashboardRowOutcome.Done };

    private static DashboardRowResult _Skipped(string id, string reason) =>
        new()
        {
            Id = id,
            Outcome = DashboardRowOutcome.Skipped,
            Reason = reason,
        };

    private static DashboardRowResult _Failed(string id, string reason) =>
        new()
        {
            Id = id,
            Outcome = DashboardRowOutcome.Failed,
            Reason = reason,
        };

    private static DashboardCommandAvailability _Answer(ObjectRef target, string commandId, string? refusal) =>
        new()
        {
            Target = target,
            CommandId = commandId,
            Allowed = refusal is null,
            DisabledReason = refusal,
        };

    private readonly record struct RunRefusals(
        string? Requeue,
        string? Cancel,
        string? Delete,
        string? Edit,
        string? Duplicate,
        string? ForceDelete
    );
}
