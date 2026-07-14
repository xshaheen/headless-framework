// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Jobs.Enums;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Describes a generated <c>[JobFunction]</c> without exposing its execution delegate.
/// </summary>
/// <remarks>
/// A <see langword="null"/> <see cref="RequestType"/> identifies a requestless function. The function name remains
/// the durable identity persisted with scheduled jobs.
/// </remarks>
[PublicAPI]
public sealed record JobFunctionDescriptor
{
    /// <summary>Creates a descriptor for a generated job function.</summary>
    /// <param name="functionName">The unique durable name of the function.</param>
    /// <param name="requestType">The request payload type, or <see langword="null"/> for a requestless function.</param>
    /// <param name="cronExpression">
    /// The six-field cron expression, an <c>IConfiguration</c> key prefixed with <c>%</c>, or
    /// <see cref="string.Empty"/> for a time job.
    /// </param>
    /// <param name="priority">The scheduling priority generated from <c>[JobFunction]</c>.</param>
    /// <param name="maxConcurrency">
    /// The maximum concurrent executions on one node; <c>0</c> means the global scheduler limit applies.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="functionName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="cronExpression"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException">
    /// <paramref name="priority"/> is not a defined <see cref="JobPriority"/> value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrency"/> is negative.</exception>
    public JobFunctionDescriptor(
        string functionName,
        Type? requestType,
        string cronExpression,
        JobPriority priority,
        int maxConcurrency
    )
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException(
                "The function name cannot be null, empty, or whitespace.",
                nameof(functionName)
            );
        }

        ArgumentNullException.ThrowIfNull(cronExpression);

        if (!Enum.IsDefined(priority))
        {
            throw new InvalidEnumArgumentException(nameof(priority), (int)priority, typeof(JobPriority));
        }

        if (maxConcurrency < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Maximum concurrency cannot be negative.");
        }

        FunctionName = functionName;
        RequestType = requestType;
        CronExpression = cronExpression;
        Priority = priority;
        MaxConcurrency = maxConcurrency;
    }

    /// <summary>The unique durable name persisted with scheduled jobs.</summary>
    public string FunctionName { get; }

    /// <summary>The request payload type, or <see langword="null"/> when the function is requestless.</summary>
    public Type? RequestType { get; }

    /// <summary>
    /// The six-field cron expression, a configuration key prefixed with <c>%</c>, or <see cref="string.Empty"/> for a
    /// time job.
    /// </summary>
    public string CronExpression { get; }

    /// <summary>The immutable scheduling priority generated from <c>[JobFunction]</c>.</summary>
    public JobPriority Priority { get; }

    /// <summary>The maximum concurrent executions on one node; <c>0</c> means the global limit applies.</summary>
    public int MaxConcurrency { get; }
}
