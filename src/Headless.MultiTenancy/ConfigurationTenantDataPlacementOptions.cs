// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.MultiTenancy;

/// <summary>
/// Tenant data placements for the configuration-backed resolver, typically bound from a section such as
/// <c>Headless:MultiTenancy:DataPlacement</c>. Bound once at startup: a change requires a process restart.
/// </summary>
[PublicAPI]
public sealed class ConfigurationTenantDataPlacementOptions
{
    /// <summary>One entry per tenant that has its own schema or database. Tenant ids must be unique.</summary>
    public IList<ConfigurationTenantDataPlacement> Tenants { get; set; } = [];
}

/// <summary>
/// One tenant's placement as bound from configuration. A plain settable shape because
/// <see cref="TenantDataPlacement"/> validates in its constructor and has no parameterless one to bind.
/// </summary>
[PublicAPI]
public sealed class ConfigurationTenantDataPlacement
{
    /// <summary>The canonical tenant id. See <see cref="TenantInfo.Id"/>.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>The tenant's database schema, or <see langword="null"/> to keep the context's schema.</summary>
    public string? Schema { get; set; }

    /// <summary>The tenant's connection string, or <see langword="null"/> to keep the context's database.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>Validator for <see cref="ConfigurationTenantDataPlacementOptions"/>.</summary>
internal sealed class ConfigurationTenantDataPlacementOptionsValidator
    : AbstractValidator<ConfigurationTenantDataPlacementOptions>
{
    public ConfigurationTenantDataPlacementOptionsValidator()
    {
        RuleFor(x => x.Tenants).NotNull();

        RuleForEach(x => x.Tenants)
            .ChildRules(entry =>
            {
                entry.RuleFor(e => e.TenantId).NotEmpty();
                entry
                    .RuleFor(e => e)
                    .Must(e => e.Schema is not null || e.ConnectionString is not null)
                    .WithMessage("A tenant data placement needs a Schema, a ConnectionString, or both.")
                    .OverridePropertyName(nameof(ConfigurationTenantDataPlacement.Schema));
                entry.RuleFor(e => e.Schema).NotEmpty().When(e => e.Schema is not null);
                entry.RuleFor(e => e.ConnectionString).NotEmpty().When(e => e.ConnectionString is not null);
            });

        RuleFor(x => x.Tenants)
            .Must(tenants => TenantSeedUniquenessValidator.HaveUniqueValues(tenants, static entry => entry.TenantId))
            .When(x => x.Tenants is not null)
            .WithMessage("Two or more tenant data placements share the same tenant id.");
    }
}
