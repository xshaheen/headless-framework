// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal sealed record JobExecuteMiddlewareRegistration(
    string Identity,
    string? Function,
    int Priority,
    JobExecuteMiddlewareDispatch Dispatch
) : IJobMiddlewareRegistration;
