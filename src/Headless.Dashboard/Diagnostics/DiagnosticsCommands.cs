// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// The diagnostic module's commands: Fail, a live action on one run whose inverse is Requeue; Requeue, an intervention
/// on ids or a filter that needs a reason and a confirmation and is refused while a run is running; and Add note, a
/// live action with an argument schema. Requeue over a filter runs as an Operation.
/// </summary>
/// <remarks>
/// The shell enforces each command's permission, confirmation, reason, and argument schema before this class runs it,
/// so this class only applies the command to the store and publishes what changed on this node's streams.
/// </remarks>
internal sealed class DiagnosticsCommands(
    string moduleId,
    DiagnosticsStore store,
    DiagnosticsSources sources,
    IDashboardEventPublisher publisher
)
{
    /// <summary>Why Requeue refuses a running run.</summary>
    public const string RunningReason = "Running - cancel instead";

    /// <summary>The error Fail gives a run.</summary>
    public const string FailedFromDashboardError = "Failed from the dashboard";

    /// <summary>The name of Add note's argument.</summary>
    public const string NoteArgument = "text";

    /// <summary>The longest note Add note accepts.</summary>
    public const int MaxNoteLength = 500;

    private readonly string _moduleId = moduleId;

    public string FailId { get; } = moduleId + ".fail";

    public string RequeueId { get; } = moduleId + ".requeue";

    public string AddNoteId { get; } = moduleId + ".add-note";

    public IReadOnlyList<DashboardCommand> Commands { get; } = _BuildCommands(moduleId);

    public IReadOnlyList<DashboardCommandAvailability> GetAllowed(IReadOnlyList<ObjectRef> targets)
    {
        var answers = new List<DashboardCommandAvailability>(targets.Count * 3);

        foreach (var target in targets)
        {
            var run =
                string.Equals(target.Module, _moduleId, StringComparison.Ordinal)
                && string.Equals(target.Kind, DiagnosticsSources.RunKind, StringComparison.Ordinal)
                    ? store.Find(target.Id)
                    : null;

            answers.Add(_Answer(target, FailId, run is null ? "Run not found" : _FailRefusal(run)));
            answers.Add(_Answer(target, RequeueId, run is null ? "Run not found" : _RequeueRefusal(run)));
            answers.Add(_Answer(target, AddNoteId, run is null ? "Run not found" : null));
        }

        return answers;
    }

    public DashboardCommandResult Execute(DashboardCommandRequest request)
    {
        var writes = request.Ids.Select(id => _Apply(request, id)).ToArray();

        _Publish(writes);

        return new DashboardCommandResult { Rows = [.. writes.Select(static write => write.Row)] };
    }

    /// <summary>
    /// Freezes a requeue over a filter. Only Requeue runs over a filter; any other command's scope is empty.
    /// </summary>
    public DashboardOperationScope Prepare(DashboardOperationScopeRequest request)
    {
        if (!string.Equals(request.CommandId, RequeueId, StringComparison.Ordinal))
        {
            return new DashboardOperationScope
            {
                Count = 0,
                Sample = [],
                HighWaterMark = "0",
            };
        }

        var (count, sample, mark) = store.PrepareRequeue(
            DiagnosticsRunFilter.Parse(request.Filter),
            request.CountLimit,
            request.SampleSize
        );

        return new DashboardOperationScope
        {
            Count = count,
            Sample = [.. sample.Select(sources.ToObject)],
            HighWaterMark = mark.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Requeues the next chunk of a frozen scope, walking failed runs in the order they failed.</summary>
    /// <exception cref="ArgumentException">The mark or cursor is not one this module returned.</exception>
    public DashboardOperationChunk RunChunk(DashboardOperationChunkRequest request)
    {
        if (!string.Equals(request.CommandId, RequeueId, StringComparison.Ordinal))
        {
            return new DashboardOperationChunk { Rows = [], NextCursor = null };
        }

        var mark = _ParseSequence(request.HighWaterMark, nameof(request.HighWaterMark));
        var cursor = request.Cursor is null ? 0 : _ParseSequence(request.Cursor, nameof(request.Cursor));

        var (writes, next) = store.RequeueChunk(
            DiagnosticsRunFilter.Parse(request.Filter),
            mark,
            cursor,
            request.ChunkSize
        );

        _Publish(writes);

        return new DashboardOperationChunk
        {
            Rows = [.. writes.Select(static write => write.Row)],
            NextCursor = next?.ToString(CultureInfo.InvariantCulture),
        };
    }

    private StoreWrite _Apply(DashboardCommandRequest request, string id)
    {
        if (string.Equals(request.CommandId, FailId, StringComparison.Ordinal))
        {
            return store.TryFail(id, FailedFromDashboardError);
        }

        if (string.Equals(request.CommandId, RequeueId, StringComparison.Ordinal))
        {
            return store.TryRequeue(id, long.MaxValue);
        }

        if (
            string.Equals(request.CommandId, AddNoteId, StringComparison.Ordinal)
            && request.Arguments.TryGetValue(NoteArgument, out var text)
            && text.ValueKind == JsonValueKind.String
        )
        {
            return store.TryAddNote(id, text.GetString()!);
        }

        return new StoreWrite
        {
            Id = id,
            Outcome = DashboardRowOutcome.Failed,
            Reason = $"Unknown command '{request.CommandId}'",
        };
    }

    // Streams on this node show the change at once; other nodes read it from the shared store.
    private void _Publish(IEnumerable<StoreWrite> writes)
    {
        foreach (var write in writes)
        {
            if (write.Run is { } run)
            {
                publisher.Publish(sources.ToChange(run, write.AffectsAttention));
            }
        }
    }

    private static DashboardCommand[] _BuildCommands(string id)
    {
        return
        [
            new()
            {
                Id = id + ".fail",
                Label = "Fail",
                ObjectKind = DiagnosticsSources.RunKind,
                InverseCommandId = id + ".requeue",
            },
            new()
            {
                Id = id + ".requeue",
                Label = "Requeue",
                ObjectKind = DiagnosticsSources.RunKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
            },
            new()
            {
                Id = id + ".add-note",
                Label = "Add note",
                ObjectKind = DiagnosticsSources.RunKind,
                Arguments =
                [
                    new()
                    {
                        Name = NoteArgument,
                        Label = "Note",
                        Kind = DashboardArgumentKind.Text,
                        Required = true,
                        MaxLength = MaxNoteLength,
                    },
                ],
            },
        ];
    }

    private static string? _FailRefusal(DiagnosticsRun run)
    {
        return run.Status == DiagnosticsRunStatus.Failed ? "Already failed" : null;
    }

    private static string? _RequeueRefusal(DiagnosticsRun run)
    {
        return run.Status switch
        {
            DiagnosticsRunStatus.Failed => null,
            DiagnosticsRunStatus.Running => RunningReason,
            _ => "Not failed",
        };
    }

    private static DashboardCommandAvailability _Answer(ObjectRef target, string commandId, string? refusal)
    {
        return new DashboardCommandAvailability
        {
            Target = target,
            CommandId = commandId,
            Allowed = refusal is null,
            DisabledReason = refusal,
        };
    }

    private static long _ParseSequence(string value, string name)
    {
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            ? sequence
            : throw new ArgumentException($"'{value}' is not a diagnostic store sequence.", name);
    }
}
