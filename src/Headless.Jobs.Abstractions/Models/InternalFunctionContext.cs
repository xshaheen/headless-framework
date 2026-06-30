// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

public class InternalFunctionContext
{
    // Cached function delegate, priority, and max concurrency for performance optimization
    // Eliminates dictionary lookups during execution
    public JobFunctionDelegate CachedDelegate { get; set; } = null!;
    public JobPriority CachedPriority { get; set; }
    public int CachedMaxConcurrency { get; set; }

    public required string FunctionName { get; set; }
    public Guid JobId { get; set; }
    public Guid? ParentId { get; set; }
    public JobType Type { get; set; }
    public int Retries { get; set; }
    public int RetryCount { get; set; }
    public JobStatus Status { get; set; }
    public long ElapsedTime { get; set; }
    public string? ExceptionDetails { get; set; }
    public DateTime ExecutedAt { get; set; }
    public int[]? RetryIntervals { get; set; }
    public bool ReleaseLock { get; set; }

    // #1/#463 transient runtime flag (not persisted, not part of ParametersToUpdate): set by the renewal loop when it
    // cancels the job on lease loss, so the cancellation handler leaves the row InProgress for the stalled-reclaim /
    // OnNodeDeath sweep instead of writing a terminal Cancelled (which would drop a still-valid Retry job).
    public bool LeaseLost { get; set; }

    public DateTime ExecutionTime { get; set; }
    public RunCondition RunCondition { get; set; }
    public List<InternalFunctionContext> TimeJobChildren { get; } = [];

    public HashSet<string> PropertiesToUpdate { get; } = [];

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(InternalFunctionContext))]
    public InternalFunctionContext SetProperty<T>(Expression<Func<InternalFunctionContext, T>> property, T value)
    {
        if (property.Body is MemberExpression { Member: PropertyInfo prop })
        {
            prop.SetValue(this, value);
            PropertiesToUpdate.Add(prop.Name);
        }
        else
        {
            throw new ArgumentException("Expression must point to a property", nameof(property));
        }

        return this;
    }

    public InternalFunctionContext ResetUpdateProps()
    {
        PropertiesToUpdate.Clear();
        return this;
    }
}
