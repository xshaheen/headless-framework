// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Middleware invoked once for every handler execution attempt.</summary>
public interface IJobExecuteMiddleware
{
    /// <summary>Invokes this middleware and, when accepted, the next pipeline component.</summary>
    Task InvokeAsync(JobExecuteContext context, JobExecuteNext next, CancellationToken cancellationToken = default);
}
