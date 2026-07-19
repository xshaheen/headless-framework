// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.FluentValidation;
using Headless.Validators;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace FluentValidation;

/// <summary>FluentValidation extension rules for validating that a string is a defined enum member name.</summary>
[PublicAPI]
public static class EnumValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    /// <summary>
    /// Validates that the value is the name of a defined member of <paramref name="enumType"/>
    /// (for example <c>"Active"</c>). Numeric strings are rejected even when they correspond to a
    /// defined value. Passes <see langword="null"/> through without failure.
    /// </summary>
    /// <param name="rule">The rule builder to extend.</param>
    /// <param name="enumType">The enum type whose member names are accepted.</param>
    /// <param name="ignoreCase">When <see langword="true"/>, member-name matching is case-insensitive. Defaults to <see langword="false"/>.</param>
    /// <returns>The rule builder options for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="enumType"/> is not an enum type.</exception>
    public static IRuleBuilderOptions<T, string> EnumName<T>(
        this IRuleBuilder<T, string> rule,
        Type enumType,
        bool ignoreCase = false
    )
#nullable restore
    {
        // Resolve (and validate) the name set once at registration: a non-enum type fails here rather
        // than per validation, and each validation is then a single FrozenSet lookup.
        var names = EnumNameValidator.GetNames(enumType, ignoreCase);

        return rule.Must(value => value is null || names.Contains(value))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Enums.InvalidName());
    }
}
