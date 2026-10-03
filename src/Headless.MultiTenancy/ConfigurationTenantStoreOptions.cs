// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.MultiTenancy;

/// <summary>
/// Seed data for the configuration-backed <see cref="ITenantStore"/>, typically bound from a
/// section such as <c>Headless:MultiTenancy:Tenants</c>. Bound once via the options system at startup:
/// a configuration change after startup does not affect already-resolved tenants — reload
/// requires a process restart.
/// </summary>
[PublicAPI]
public sealed class ConfigurationTenantStoreOptions
{
    /// <summary>
    /// The seeded tenants. Identifiers are normalized (trimmed, lowercased) by the store at startup;
    /// two seeds whose identifiers normalize to the same value fail startup.
    /// </summary>
    public IList<ConfigurationTenantSeed> Tenants { get; set; } = [];
}

/// <summary>Validator for <see cref="ConfigurationTenantStoreOptions"/>.</summary>
internal sealed class ConfigurationTenantStoreOptionsValidator : AbstractValidator<ConfigurationTenantStoreOptions>
{
    // Mirrors TenantCatalogOptions' compiled defaults (MaxIdentifierLength = 63, IdentifierPattern =
    // RegexPatterns.Slug). A seed whose identifier cannot match the framework's default shape could
    // never be reached by identifier-based resolution (the default resolution pipeline rejects the raw
    // input before any store lookup), so rejecting it at startup surfaces the dead configuration immediately instead of
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
        + "Headless.MultiTenancy configuration store requires unique normalized identifiers.";

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
