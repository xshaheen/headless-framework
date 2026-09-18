// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>
/// FluentValidation extensions for validating provider-bound storage identifiers (schema names,
/// table names) against the per-provider regex + length cap published in
/// <see cref="StorageIdentifier"/>. Encapsulates the
/// <c>.NotEmpty().Matches(pattern).MaximumLength(maxLength)</c> trio so the 12 provider Setup
/// validators stay one-line each and never drift apart on rule order or message wording.
/// </summary>
[PublicAPI]
public static class HeadlessStorageIdentifierValidators
{
    /// <summary>
    /// Validates a schema or table name against the identifier rules of <paramref name="provider" />. Provider
    /// setups that know their dialect use this one rule; the provider-named helpers below are shorthands for it.
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
    /// Validates a PostgreSQL unquoted identifier (schema/table name): leading letter or
    /// underscore, then letters / digits / underscores, capped at NAMEDATALEN - 1 = 63 chars.
    /// </summary>
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    public static IRuleBuilderOptions<T, string> IsValidPostgreSqlIdentifier<T>(this IRuleBuilder<T, string> rule)
#nullable restore
    {
        return rule.IsValidIdentifierFor(StorageProvider.PostgreSql);
    }

    /// <summary>
    /// Validates a SQL Server regular identifier (schema/table name): leading letter or
    /// underscore, then letters / digits / underscores / <c>@</c> / <c>$</c> / <c>#</c>, capped
    /// at 128 chars.
    /// </summary>
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    public static IRuleBuilderOptions<T, string> IsValidSqlServerIdentifier<T>(this IRuleBuilder<T, string> rule)
#nullable restore
    {
        return rule.IsValidIdentifierFor(StorageProvider.SqlServer);
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
