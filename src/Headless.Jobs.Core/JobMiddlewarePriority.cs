// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Ordering constants for Jobs middleware. Lower values run first.</summary>
public static class JobMiddlewarePriority
{
    /// <summary>Runs before the default middleware position.</summary>
    public const int Early = -1000;

    /// <summary>
    /// Position of the framework tenancy middleware. Ties with <see cref="Early"/>: no framework middleware registers
    /// at <see cref="Early"/> today, so a tie is possible only with consumer-declared middleware and resolves
    /// deterministically by ordinal middleware identity (<c>{assembly}:{fully-qualified-type}</c>). Relative order
    /// against consumer <see cref="Early"/> middleware is not contractual.
    /// </summary>
    public const int Tenancy = -1000;

    /// <summary>The default middleware position.</summary>
    public const int Default = 0;

    /// <summary>Runs after the default middleware position.</summary>
    public const int Late = 1000;
}
