// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Dashboard;

/// <summary>
/// The default operator-log sink: writes each entry as one structured <c>ILogger</c> event under the
/// <c>Headless.Dashboard.OperatorLog</c> category, so the host's log pipeline keeps the audit trail without any storage
/// of the dashboard's own.
/// </summary>
internal sealed class LoggerOperatorLog(ILoggerFactory loggerFactory) : IDashboardOperatorLog
{
    /// <summary>The log category every entry is written under, so a host can route the audit trail on its own.</summary>
    public const string Category = "Headless.Dashboard.OperatorLog";

    private readonly ILogger _logger = loggerFactory.CreateLogger(Category);

    public ValueTask WriteAsync(OperatorLogEntry entry, CancellationToken cancellationToken)
    {
        _logger.OperatorAction(
            entry.Actor,
            entry.Channel,
            entry.Action,
            entry.CommandId,
            entry.Module,
            entry.Count,
            entry.Done,
            entry.Skipped,
            entry.Failed,
            entry.Reason,
            entry.OperationId,
            entry.Ids,
            entry.Scope,
            entry.Cursor
        );

        return ValueTask.CompletedTask;
    }
}

internal static partial class LoggerOperatorLogMessages
{
    [LoggerMessage(
        EventId = 2220,
        EventName = "DashboardOperatorAction",
        Level = LogLevel.Information,
        Message = "Dashboard operator {Actor} ({Channel}) {Action} {CommandId} in module {ModuleId}: {Count} rows, "
            + "{Done} done, {Skipped} skipped, {Failed} failed; reason: {Reason}; operation {OperationId}; "
            + "ids {Ids}; scope {Scope}; cursor {Cursor}"
    )]
    internal static partial void OperatorAction(
        this ILogger logger,
        string actor,
        DashboardChannels channel,
        OperatorLogAction action,
        string commandId,
        string moduleId,
        long count,
        long done,
        long skipped,
        long failed,
        string? reason,
        string? operationId,
        IReadOnlyList<string> ids,
        string? scope,
        string? cursor
    );
}
