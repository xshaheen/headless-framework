// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.MultiTenancy;

/// <summary>Options for resolving tenant data placements.</summary>
[PublicAPI]
public sealed class TenantDataPlacementOptions
{
    /// <summary>
    /// How long a placement returned by a custom resolver (<c>UseResolver&lt;T&gt;()</c>) stays cached in process.
    /// Bounds how long a node keeps routing a moved tenant to its previous schema or database. A tenant with no
    /// placement is never cached, so a newly provisioned tenant routes as soon as its placement exists.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Validator for <see cref="TenantDataPlacementOptions"/>.</summary>
internal sealed class TenantDataPlacementOptionsValidator : AbstractValidator<TenantDataPlacementOptions>
{
    public TenantDataPlacementOptionsValidator()
    {
        RuleFor(x => x.CacheExpiration).GreaterThan(TimeSpan.Zero);
    }
}
