// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Mutable scheduling state exposed to schedule middleware.</summary>
public sealed class JobScheduleContext(JobFunctionDescriptor descriptor, BaseJobEntity job, IServiceProvider services)
{
    /// <summary>The resolved immutable descriptor for the submitted job.</summary>
    public JobFunctionDescriptor Descriptor { get; } = descriptor;

    /// <summary>The time or cron entity that will be validated and persisted after the pipeline completes.</summary>
    public BaseJobEntity Job { get; } = job;

    /// <summary>The bounded scheduling-invocation service provider.</summary>
    public IServiceProvider Services { get; } = services;
}
