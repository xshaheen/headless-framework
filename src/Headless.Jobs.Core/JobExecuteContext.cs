// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Per-attempt execution state exposed to execute middleware.</summary>
public sealed class JobExecuteContext(
    JobFunctionDescriptor descriptor,
    JobExecutionState execution,
    JobContext functionContext,
    int attempt,
    IServiceProvider services
)
{
    /// <summary>The resolved immutable descriptor for the executing job.</summary>
    public JobFunctionDescriptor Descriptor { get; } = descriptor;

    /// <summary>The mutable execution state owned by the existing execution flow.</summary>
    public JobExecutionState Execution { get; } = execution;

    /// <summary>The existing handler context for this attempt.</summary>
    public JobContext FunctionContext { get; } = functionContext;

    /// <summary>Zero-based retry attempt number.</summary>
    public int Attempt { get; } = attempt;

    /// <summary>The bounded attempt service provider.</summary>
    public IServiceProvider Services { get; } = services;
}
