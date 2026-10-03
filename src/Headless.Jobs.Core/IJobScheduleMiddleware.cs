// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Middleware invoked once for every submitted scheduling entity.</summary>
public interface IJobScheduleMiddleware
{
    /// <summary>Invokes this middleware and, when accepted, the next pipeline component.</summary>
    Task InvokeAsync(JobScheduleContext context, JobScheduleNext next, CancellationToken cancellationToken = default);
}
