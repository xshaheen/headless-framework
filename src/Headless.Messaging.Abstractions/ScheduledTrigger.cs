// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents a scheduled trigger message consumed via the messaging infrastructure.
/// Replaces <c>TickerFunctionContext</c> for scheduling use cases routed through the message bus.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="ScheduledTrigger"/> as the message type in an <see cref="IConsume{TMessage}"/>
/// implementation to handle scheduled or recurring jobs:
/// </para>
/// <code>
/// public sealed class DailyReportHandler : IConsume&lt;ScheduledTrigger&gt;
/// {
///     public async ValueTask Consume(ConsumeContext&lt;ScheduledTrigger&gt; context, CancellationToken cancellationToken)
///     {
///         var trigger = context.Message;
///         // trigger.JobName, trigger.ScheduledTime, trigger.Attempt, etc.
///     }
/// }
/// </code>
/// </remarks>
public sealed record ScheduledTrigger
{
    /// <summary>
    /// Gets the UTC time this job was scheduled to run.
    /// </summary>
    /// <value>
    /// For one-off jobs this is the requested execution time.
    /// For cron jobs this is the occurrence time that fired the trigger.
    /// </value>
    public required DateTimeOffset ScheduledTime { get; init; }

    /// <summary>
    /// Gets the unique name that identifies the scheduled job definition.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the 1-based attempt number for this execution.
    /// </summary>
    /// <value>
    /// Starts at 1 for the first attempt and increments on each retry.
    /// </value>
    public required int Attempt { get; init; }

    /// <summary>
    /// Gets the cron expression that produced this trigger, if the job is cron-based.
    /// </summary>
    /// <value>
    /// A standard cron expression (e.g. <c>0 */5 * * *</c>), or <c>null</c> for one-off jobs.
    /// </value>
    public string? CronExpression { get; init; }

    /// <summary>
    /// Gets the identifier of a parent job that spawned this trigger, if any.
    /// </summary>
    /// <value>
    /// The parent job's unique identifier, or <c>null</c> when the trigger has no parent.
    /// </value>
    public Guid? ParentJobId { get; init; }

    /// <summary>
    /// Gets an optional opaque payload associated with the trigger.
    /// </summary>
    /// <value>
    /// A free-form string (typically JSON) carrying job-specific data, or <c>null</c> when no payload is needed.
    /// </value>
    public string? Payload { get; init; }
}
