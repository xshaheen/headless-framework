// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.MultiTenancy;

/// <summary>Seed data for the in-memory <see cref="ITenantStore"/>.</summary>
[PublicAPI]
public sealed class InMemoryTenantStoreOptions
{
    /// <summary>
    /// The seeded tenants. Identifiers are normalized (trimmed, lowercased) by the store at startup;
    /// two seeds whose identifiers normalize to the same value fail startup (R20).
    /// </summary>
    public IList<TenantInfo> Tenants { get; set; } = [];
}

/// <summary>Validator for <see cref="InMemoryTenantStoreOptions"/>.</summary>
internal sealed class InMemoryTenantStoreOptionsValidator : AbstractValidator<InMemoryTenantStoreOptions>
{
    public InMemoryTenantStoreOptionsValidator()
    {
        RuleFor(x => x.Tenants).NotNull();

        RuleFor(x => x.Tenants)
            .Must(_HaveUniqueNormalizedIdentifiers)
            .When(x => x.Tenants is not null)
            .WithMessage(_DuplicateIdentifierMessage);

        RuleFor(x => x.Tenants).Must(_HaveUniqueIds).When(x => x.Tenants is not null).WithMessage(_DuplicateIdMessage);
    }

    private const string _DuplicateIdentifierMessage =
        "Two or more seeded tenants normalize to the same identifier. "
        + "Headless.MultiTenancy in-memory store requires unique normalized identifiers (R20).";

    private const string _DuplicateIdMessage =
        "Two or more seeded tenants share the same canonical tenant id. "
        + "Headless.MultiTenancy in-memory store requires unique tenant ids.";

    private static bool _HaveUniqueNormalizedIdentifiers(IList<TenantInfo> tenants)
    {
        return TenantSeedUniquenessValidator.HaveUniqueValues(
            tenants,
            static tenant => tenant.Identifier.Trim().ToLowerInvariant()
        );
    }

    private static bool _HaveUniqueIds(IList<TenantInfo> tenants)
    {
        return TenantSeedUniquenessValidator.HaveUniqueValues(tenants, static tenant => tenant.Id);
    }
}
