// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>One operator-log entry: who ran which command, through which channel, on what, why, and with what outcome.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record OperatorLogEntry
{
    /// <summary>Gets when the shell wrote the entry, by the registered <see cref="TimeProvider"/>.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the operator: the session's name-identifier claim, else its name, else <c>anonymous</c> when the dashboard
    /// runs without authentication.
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>
    /// Gets the channel the call claimed. It is recorded as claimed, not proven: a script holding the operator's
    /// session can claim the UI.
    /// </summary>
    public required DashboardChannels Channel { get; init; }

    /// <summary>Gets the id of the module that owns the command.</summary>
    public required string Module { get; init; }

    /// <summary>Gets the command's id, such as <c>jobs.requeue</c>.</summary>
    public required string CommandId { get; init; }

    /// <summary>Gets what the entry records: a command run over ids, an Operation's confirm, or one of its chunks.</summary>
    public required OperatorLogAction Action { get; init; }

    /// <summary>
    /// Gets the ids the entry covers: the ids a run named, or the rows a chunk acted on or skipped. Empty for a confirm.
    /// </summary>
    public IReadOnlyList<string> Ids { get; init; } = [];

    /// <summary>Gets an Operation's filter as JSON text, the scope it was confirmed for; <see langword="null"/> for a run over ids.</summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Gets the number of rows the entry covers: the ids a run named, the rows an Operation was confirmed for, or the
    /// rows a chunk returned.
    /// </summary>
    public required long Count { get; init; }

    /// <summary>Gets the operator's reason, or <see langword="null"/> when the command asks for none and none was given.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets how many rows the command changed. Zero for a confirm.</summary>
    public long Done { get; init; }

    /// <summary>Gets how many rows no longer qualified and were left alone.</summary>
    public long Skipped { get; init; }

    /// <summary>
    /// Gets how many rows the command tried to change and failed. A module that throws counts every row of the call as
    /// failed.
    /// </summary>
    public long Failed { get; init; }

    /// <summary>Gets the Operation's id, shared by its confirm and chunk entries; <see langword="null"/> for a run over ids.</summary>
    public string? OperationId { get; init; }
}

/// <summary>What an operator-log entry records.</summary>
[PublicAPI]
public enum OperatorLogAction
{
    /// <summary>A command run over the ids the call named.</summary>
    Run = 0,

    /// <summary>An Operation confirmed over a filter: its scope was frozen and counted, and nothing was changed yet.</summary>
    OperationConfirmed = 1,

    /// <summary>One chunk of an Operation.</summary>
    OperationChunk = 2,
}
