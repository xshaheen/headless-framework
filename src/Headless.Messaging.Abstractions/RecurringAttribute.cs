// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Marks a consumer class as a recurring (cron-based) scheduled job.
/// Apply to an <see cref="IConsume{TMessage}"/> implementation that handles
/// <see cref="ScheduledTrigger"/> messages on a cron schedule.
/// </summary>
/// <remarks>
/// <para>
/// The infrastructure reads this attribute during consumer registration to configure
/// the recurring schedule, retry behaviour, and concurrency guard.
/// </para>
/// <code>
/// [Recurring("0 0 */6 * * *")] // every 6 hours (6-field cron)
/// public sealed class UsageReportJob : IConsume&lt;ScheduledTrigger&gt;
/// {
///     public async ValueTask Consume(ConsumeContext&lt;ScheduledTrigger&gt; context, CancellationToken cancellationToken)
///     {
///         // ...
///     }
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RecurringAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="RecurringAttribute"/> with the given cron expression.
    /// </summary>
    /// <param name="cronExpression">
    /// A 6-field cron expression (second, minute, hour, day-of-month, month, day-of-week).
    /// </param>
    public RecurringAttribute(string cronExpression)
    {
        CronExpression = cronExpression;
    }

    /// <summary>
    /// Gets the 6-field cron expression that defines the recurrence schedule.
    /// </summary>
    public string CronExpression { get; }

    /// <summary>
    /// Gets or sets an optional human-readable name for the job.
    /// When <c>null</c>, the infrastructure derives a name from the consumer type.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the IANA time-zone identifier used to evaluate the cron expression.
    /// When <c>null</c>, UTC is used.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Gets or sets the retry intervals (in seconds) between successive retry attempts.
    /// When <c>null</c>, the infrastructure's default retry policy applies.
    /// </summary>
    public int[]? RetryIntervals { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a new occurrence should be skipped
    /// if the previous execution is still running. Default is <c>true</c>.
    /// </summary>
    public bool SkipIfRunning { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout in seconds for job execution.
    /// When set to 0, no timeout is enforced. Default is 0.
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the strategy for handling missed scheduled executions.
    /// Default is <see cref="MisfireStrategy.FireImmediately"/>.
    /// </summary>
    public MisfireStrategy MisfireStrategy { get; set; } = MisfireStrategy.FireImmediately;
}
