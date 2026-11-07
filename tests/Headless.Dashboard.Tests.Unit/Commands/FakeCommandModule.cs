// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Tests.Aggregation;

namespace Tests.Commands;

/// <summary>
/// A diagnostic-like module over a <see cref="FakeRunStore"/>: requeue (confirmed, with a reason), a UI-only command
/// with an argument schema, an API-only read command, a purge behind a named capability, and a pause with an inverse.
/// </summary>
internal sealed class FakeCommandModule(FakeRunStore store)
    : IDashboardModule,
        IDashboardCommandSource,
        IDashboardOperationRunner,
        IDashboardPeerAccess
{
    /// <summary>The host policy a peer call to this module must pass.</summary>
    public const string PeersPolicy = "DiagPeers";

    public const string Requeue = "diag.requeue";
    public const string RetryLater = "diag.retry-later";
    public const string Export = "diag.export";
    public const string Purge = "diag.purge";
    public const string Pause = "diag.pause";
    public const string Resume = "diag.resume";

    public string Id => "diag";

    public string DisplayName => "Diagnostics";

    public string PeerAuthenticationScheme => PeerKeyHandler.SchemeName;

    public string PeerPolicy => PeersPolicy;

    public DashboardModuleAssets Assets { get; } =
        new()
        {
            Files = new SingleFileProvider(
                "manifest.json",
                """{"id":"diag","sdkVersion":"1.0.0","entry":"e.js","product":"platform"}"""
            ),
        };

    public IReadOnlyList<DashboardCommand> Commands { get; } =
    [
        new()
        {
            Id = Requeue,
            Label = "Requeue",
            ObjectKind = "run",
            Confirmation = DashboardConfirmation.Confirm,
            RequiresReason = true,
        },
        new()
        {
            Id = RetryLater,
            Label = "Retry later",
            ObjectKind = "run",
            Visibility = DashboardChannels.Ui,
            Arguments =
            [
                new()
                {
                    Name = "delayMinutes",
                    Label = "Delay (minutes)",
                    Kind = DashboardArgumentKind.WholeNumber,
                    Required = true,
                    Minimum = 1,
                    Maximum = 60,
                },
                new()
                {
                    Name = "priority",
                    Label = "Priority",
                    Kind = DashboardArgumentKind.Text,
                    AllowedValues = ["low", "high"],
                },
            ],
        },
        new()
        {
            Id = Export,
            Label = "Export",
            ObjectKind = "run",
            Permission = DashboardCommandPermission.View,
            Visibility = DashboardChannels.Api,
        },
        new()
        {
            Id = Purge,
            Label = "Purge",
            ObjectKind = "run",
            Capability = Purge,
            Confirmation = DashboardConfirmation.Confirm,
        },
        new()
        {
            Id = Pause,
            Label = "Pause",
            ObjectKind = "run",
            Confirmation = DashboardConfirmation.Confirm,
            InverseCommandId = Resume,
        },
        new()
        {
            Id = Resume,
            Label = "Resume",
            ObjectKind = "run",
        },
    ];

    /// <summary>Every command request the module ran, in order.</summary>
    public List<DashboardCommandRequest> Executed { get; } = [];

    /// <summary>Every chunk request the module ran, in order.</summary>
    public List<DashboardOperationChunkRequest> Chunks { get; } = [];

    /// <summary>When set, every chunk request is recorded and then throws this, as a module failing mid-chunk would.</summary>
    public Exception? ChunkFailure { get; set; }

    public static DashboardObject ToObject(FakeRun run)
    {
        return new DashboardObject
        {
            Target = new ObjectRef
            {
                Module = "diag",
                Kind = "run",
                Id = run.Id,
            },
            Title = run.Id,
            Status = run.State.ToString(),
        };
    }

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        endpoints.Routes.MapGet("/items", static () => "items");
    }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    )
    {
        var answers = new List<DashboardCommandAvailability>();

        foreach (var target in request.Targets)
        {
            var running = store.Get(target.Id).State == FakeRunState.Running;

            foreach (var command in Commands)
            {
                var refused = running && string.Equals(command.Id, Requeue, StringComparison.Ordinal);
                answers.Add(
                    new DashboardCommandAvailability
                    {
                        Target = target,
                        CommandId = command.Id,
                        Allowed = !refused,
                        DisabledReason = refused ? "Running - cancel instead" : null,
                    }
                );
            }
        }

        return Task.FromResult<IReadOnlyList<DashboardCommandAvailability>>(answers);
    }

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (Executed)
        {
            Executed.Add(request);
        }

        IReadOnlyList<DashboardRowResult> rows = string.Equals(request.CommandId, Requeue, StringComparison.Ordinal)
            ? [.. request.Ids.Select(store.Requeue)]
            :
            [
                .. request.Ids.Select(static id => new DashboardRowResult
                {
                    Id = id,
                    Outcome = DashboardRowOutcome.Done,
                }),
            ];

        return Task.FromResult(new DashboardCommandResult { Rows = rows });
    }

    public Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(store.Prepare(_Queue(request.Filter), request.CountLimit, request.SampleSize));
    }

    public Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    )
    {
        lock (Chunks)
        {
            Chunks.Add(request);
        }

        if (ChunkFailure is { } failure)
        {
            throw failure;
        }

        var mark = long.Parse(request.HighWaterMark, CultureInfo.InvariantCulture);

        return Task.FromResult(store.Chunk(_Queue(request.Filter), mark, request.Cursor, request.ChunkSize));
    }

    private static string? _Queue(JsonElement filter)
    {
        return filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty("queue", out var queue)
            ? queue.GetString()
            : null;
    }
}

internal sealed class FakeCommandModuleOptions : DashboardModuleOptions;

/// <summary>Switches the host's operate and purge policies off between requests, as revoking a grant would.</summary>
internal sealed class PermissionGate
{
    public bool Operate { get; set; } = true;

    public bool Purge { get; set; } = true;
}

/// <summary>An operator-log sink that keeps every entry, standing in for a host's own audit store.</summary>
internal sealed class RecordingOperatorLog : IDashboardOperatorLog
{
    private readonly Lock _lock = new();
    private readonly List<OperatorLogEntry> _entries = [];

    public IReadOnlyList<OperatorLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ValueTask WriteAsync(OperatorLogEntry entry, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }

        return ValueTask.CompletedTask;
    }
}
