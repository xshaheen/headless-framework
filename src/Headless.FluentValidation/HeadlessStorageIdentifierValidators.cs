// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>
/// FluentValidation extensions for validating provider-bound storage identifiers (schema names,
/// table names) against the per-provider regex + length cap published in
/// <see cref="StorageIdentifier"/>. Encapsulates the
/// <c>.NotEmpty().Matches(pattern).MaximumLength(maxLength)</c> trio so every provider setup validator stays
/// one line and none drifts on rule order or message wording.
/// </summary>
[PublicAPI]
public static class HeadlessStorageIdentifierValidators
{
    /// <summary>
    /// Validates a schema or table name against the identifier rules of <paramref name="provider" />. Provider
    /// setups that know their dialect use this rule; EF setups that learn the dialect only at runtime use the
    /// cross-provider rule below.
    /// </summary>
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    public static IRuleBuilderOptions<T, string> IsValidIdentifierFor<T>(
        this IRuleBuilder<T, string> rule,
        StorageProvider provider
    )
#nullable restore
    {
        var (pattern, maxLength) = StorageIdentifier.For(provider);

        return rule.NotEmpty().Matches(pattern).MaximumLength(maxLength);
    }

    /// <summary>
    /// Validates a cross-provider storage identifier. Uses the more permissive SQL Server pattern
    /// (a superset of PostgreSQL's character set) and the larger SQL Server length cap so EF Core
    /// validators accept either provider's identifiers; the underlying database surfaces any
    /// provider-specific length/character issues at migration time.
    /// </summary>
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    public static IRuleBuilderOptions<T, string> IsValidCrossProviderIdentifier<T>(this IRuleBuilder<T, string> rule)
#nullable restore
    {
        return rule.NotEmpty()
            .Matches(StorageIdentifier.SqlServer.IdentifierPattern)
            .MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
    }
}
