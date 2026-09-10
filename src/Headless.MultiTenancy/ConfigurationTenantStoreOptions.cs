// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.MultiTenancy;

/// <summary>
/// Seed data for the configuration-backed <see cref="ITenantStore"/> (R16), typically bound from a
/// section such as <c>Headless:MultiTenancy:Tenants</c>. Bound once via the options system at startup
/// (KTD7): a configuration change after startup does not affect already-resolved tenants — reload
/// requires a process restart.
/// </summary>
[PublicAPI]
public sealed class ConfigurationTenantStoreOptions
{
    /// <summary>
    /// The seeded tenants. Identifiers are normalized (trimmed, lowercased) by the store at startup;
    /// two seeds whose identifiers normalize to the same value fail startup (R20).
    /// </summary>
    public IList<ConfigurationTenantSeed> Tenants { get; set; } = [];
}

/// <summary>
/// A single tenant seed as bound from configuration. A plain, publicly settable shape — rather than
/// binding <see cref="TenantInfo"/> directly — because <see cref="TenantInfo"/> exposes no
/// parameterless constructor for the options binder to target. <see cref="ConfigurationTenantStore"/>
/// converts each bound seed into a <see cref="TenantInfo"/> through its normal validating constructor
/// (R16: the domain type itself is never constructed through uninitialized-object reflection).
/// </summary>
[PublicAPI]
public sealed class ConfigurationTenantSeed
{
    /// <summary>The canonical tenant id. See <see cref="TenantInfo.Id"/>.</summary>
    public string Id { get; set; } = "";

    /// <summary>The public-facing tenant identifier. See <see cref="TenantInfo.Identifier"/>.</summary>
    public string Identifier { get; set; } = "";

    /// <summary>The tenant's display name, or <see langword="null"/> when not set.</summary>
    public string? Name { get; set; }

    /// <summary>Whether the tenant is enabled. Default: <see langword="true"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Read-along extra properties, bound from nested configuration keys under this seed's
    /// <c>ExtraProperties</c> section (for example <c>Tenants:0:ExtraProperties:Region</c>).
    /// Configuration leaf values are always strings; each entry is copied as-is into the resulting
    /// <see cref="Primitives.ExtraProperties"/> bag.
    /// </summary>
    public IDictionary<string, string> ExtraProperties { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Validator for <see cref="ConfigurationTenantStoreOptions"/>.</summary>
internal sealed class ConfigurationTenantStoreOptionsValidator : AbstractValidator<ConfigurationTenantStoreOptions>
{
    // Mirrors TenantCatalogOptions' compiled defaults (MaxIdentifierLength = 63, IdentifierPattern =
    // RegexPatterns.Slug). A seed whose identifier cannot match the framework's default shape could
    // never be reached by identifier-based resolution (R21 rejects the raw input before any store
    // lookup), so rejecting it at startup surfaces the dead configuration immediately instead of
    // silently shipping an unreachable tenant. Apps that configure a custom TenantCatalogOptions
    // shape are responsible for keeping their seed identifiers compatible with it — this store does
    // not cross-reference that option in v1.
    private const int _DefaultMaxIdentifierLength = 63;

    public ConfigurationTenantStoreOptionsValidator()
    {
        RuleFor(x => x.Tenants).NotNull();

        RuleForEach(x => x.Tenants)
            .ChildRules(seed =>
            {
                seed.RuleFor(s => s.Id).NotEmpty();
                seed.RuleFor(s => s.Identifier).NotEmpty();
                seed.RuleFor(s => s.Identifier)
                    .Must(_MatchDefaultIdentifierShape)
                    .When(s => !string.IsNullOrWhiteSpace(s.Identifier))
                    .WithMessage(
                        "Tenant seed identifier '{PropertyValue}' does not match the framework's default "
                            + $"identifier shape (DNS-label form, max {_DefaultMaxIdentifierLength} characters "
                            + "after normalization)."
                    );
            });

        RuleFor(x => x.Tenants)
            .Must(_HaveUniqueNormalizedIdentifiers)
            .When(x => x.Tenants is not null)
            .WithMessage(_DuplicateIdentifierMessage);

        RuleFor(x => x.Tenants).Must(_HaveUniqueIds).When(x => x.Tenants is not null).WithMessage(_DuplicateIdMessage);
    }

    private const string _DuplicateIdentifierMessage =
        "Two or more seeded tenants normalize to the same identifier. "
        + "Headless.MultiTenancy configuration store requires unique normalized identifiers (R20).";

    private const string _DuplicateIdMessage =
        "Two or more seeded tenants share the same canonical tenant id. "
        + "Headless.MultiTenancy configuration store requires unique tenant ids.";

    private static bool _MatchDefaultIdentifierShape(string identifier)
    {
        var normalized = identifier.Trim().ToLowerInvariant();

        return normalized.Length > 0
            && normalized.Length <= _DefaultMaxIdentifierLength
            && RegexPatterns.Slug.IsMatch(normalized);
    }

    private static bool _HaveUniqueNormalizedIdentifiers(IList<ConfigurationTenantSeed> tenants)
    {
        return TenantSeedUniquenessValidator.HaveUniqueValues(
            tenants,
            static tenant => tenant.Identifier.Trim().ToLowerInvariant()
        );
    }

    private static bool _HaveUniqueIds(IList<ConfigurationTenantSeed> tenants)
    {
        return TenantSeedUniquenessValidator.HaveUniqueValues(tenants, static tenant => tenant.Id);
    }
}
