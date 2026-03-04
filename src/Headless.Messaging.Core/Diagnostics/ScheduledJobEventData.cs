// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Diagnostics;

/// <summary>
/// Event data emitted by the scheduling pipeline for DiagnosticSource subscribers.
/// </summary>
public sealed class ScheduledJobEventData
{
    public required string JobName { get; init; }
    public required Guid ExecutionId { get; init; }
    public required int Attempt { get; init; }
    public required DateTimeOffset ScheduledTime { get; init; }
    public long? OperationTimestamp { get; init; }
    public long? ElapsedTimeMs { get; init; }
    public Exception? Exception { get; init; }
}
