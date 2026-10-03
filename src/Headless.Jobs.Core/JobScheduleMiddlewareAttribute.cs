// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Declares schedule middleware globally or on a <c>[Job]</c> class in the same assembly.</summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class JobScheduleMiddlewareAttribute<TMiddleware> : Attribute
    where TMiddleware : IJobScheduleMiddleware
{
    /// <summary>
    /// Targets a function declared in another assembly. Omit for global assembly middleware or
    /// class-level middleware, whose target is the <c>[Job]</c> class it decorates.
    /// </summary>
    public string? Function { get; init; }

    /// <summary>Ordering priority; equal priorities are ordered by stable middleware identity.</summary>
    public int Priority { get; init; }
}
