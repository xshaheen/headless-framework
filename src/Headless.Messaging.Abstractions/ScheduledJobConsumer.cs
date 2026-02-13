// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;

namespace Headless.Messaging;

/// <summary>
/// Base execution context for scheduled jobs.
/// </summary>
public class ScheduledJobExecutionContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledJobExecutionContext"/>.
    /// </summary>
    /// <param name="context">The raw consume context.</param>
    public ScheduledJobExecutionContext(ConsumeContext<ScheduledTrigger> context)
    {
        RawContext = context;
    }

    /// <summary>
    /// Gets the raw scheduled-trigger consume context.
    /// </summary>
    public ConsumeContext<ScheduledTrigger> RawContext { get; }

    /// <summary>
    /// Gets the job name.
    /// </summary>
    public string JobName => RawContext.Message.JobName;

    /// <summary>
    /// Gets the scheduled execution time.
    /// </summary>
    public DateTimeOffset ScheduledTime => RawContext.Message.ScheduledTime;

    /// <summary>
    /// Gets the current attempt number (1-based).
    /// </summary>
    public int Attempt => RawContext.Message.Attempt;

    /// <summary>
    /// Gets the cron expression when this execution was triggered by a recurring schedule.
    /// </summary>
    public string? CronExpression => RawContext.Message.CronExpression;
}

/// <summary>
/// Typed execution context for scheduled jobs.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public sealed class ScheduledJobExecutionContext<TPayload>(ConsumeContext<ScheduledTrigger> context, TPayload? payload)
    : ScheduledJobExecutionContext(context)
{
    /// <summary>
    /// Gets the deserialized payload.
    /// </summary>
    public TPayload? Payload { get; } = payload;
}

/// <summary>
/// Base class for class-based scheduled jobs without a typed payload.
/// </summary>
public abstract class ScheduledJobConsumer : IConsume<ScheduledTrigger>
{
    /// <inheritdoc />
    public ValueTask Consume(ConsumeContext<ScheduledTrigger> context, CancellationToken cancellationToken)
    {
        return ExecuteAsync(new ScheduledJobExecutionContext(context), cancellationToken);
    }

    /// <summary>
    /// Executes the scheduled job.
    /// </summary>
    /// <param name="context">The scheduled job execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing job completion.</returns>
    protected abstract ValueTask ExecuteAsync(
        ScheduledJobExecutionContext context,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Base class for class-based scheduled jobs with a typed payload.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public abstract class ScheduledJobConsumer<TPayload> : IConsume<ScheduledTrigger>
{
    /// <summary>
    /// Gets serializer options used for payload deserialization.
    /// </summary>
    protected virtual JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public ValueTask Consume(ConsumeContext<ScheduledTrigger> context, CancellationToken cancellationToken)
    {
        var payload = _DeserializePayload(context.Message.Payload);
        var typedContext = new ScheduledJobExecutionContext<TPayload>(context, payload);
        return ExecuteAsync(typedContext, cancellationToken);
    }

    /// <summary>
    /// Executes the scheduled job.
    /// </summary>
    /// <param name="context">The typed scheduled job execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing job completion.</returns>
    protected abstract ValueTask ExecuteAsync(
        ScheduledJobExecutionContext<TPayload> context,
        CancellationToken cancellationToken
    );

    private TPayload? _DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        if (typeof(TPayload) == typeof(string))
        {
            return (TPayload?)(object?)payload;
        }

        return JsonSerializer.Deserialize<TPayload>(payload, SerializerOptions);
    }
}
