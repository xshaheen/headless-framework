// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal interface IJobMiddlewareRegistration
{
    string Identity { get; }
    string? Function { get; }
    int Priority { get; }
}
