// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace FluentValidation;

/// <summary>FluentValidation extension rules for identifier properties.</summary>
[PublicAPI]
public static class IdValidators
{
    /// <summary>Validates that the <see cref="Guid"/> value is not <see cref="Guid.Empty"/>.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, Guid> Id<T>(this IRuleBuilder<T, Guid> rule)
    {
        return rule.NotEqual(Guid.Empty);
    }

    /// <summary>Validates that the nullable <see cref="Guid"/> value, when present, is not <see cref="Guid.Empty"/>.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, Guid?> Id<T>(this IRuleBuilder<T, Guid?> rule)
    {
        return rule.NotEqual(Guid.Empty);
    }

#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    /// <summary>Validates that the string value is not empty.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, string> Id<T>(this IRuleBuilder<T, string> rule)
#nullable restore
    {
        return rule.NotEqual(string.Empty);
    }

    /// <summary>Validates that the integer identifier is greater than zero.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, int> Id<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.GreaterThan(0);
    }

    /// <summary>Validates that the nullable integer identifier, when present, is greater than zero.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, int?> Id<T>(this IRuleBuilder<T, int?> rule)
    {
        return rule.GreaterThan(0);
    }

    /// <summary>Validates that the long identifier is greater than zero.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, long> Id<T>(this IRuleBuilder<T, long> rule)
    {
        return rule.GreaterThan(0);
    }

    /// <summary>Validates that the nullable long identifier, when present, is greater than zero.</summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, long?> Id<T>(this IRuleBuilder<T, long?> rule)
    {
        return rule.GreaterThan(0);
    }
}
