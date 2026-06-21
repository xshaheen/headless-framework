// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using Headless.Constants;

namespace FluentValidation;

/// <summary>FluentValidation extension rules for common string format constraints.</summary>
[PublicAPI]
public static class StringsFluentValidationExtensions
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    /// <summary>
    /// Validates that the string consists entirely of digit characters (no sign, no decimal point).
    /// Passes <see langword="null"/> values through without failure.
    /// </summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, string> OnlyIntegers<T>(this IRuleBuilder<T, string> builder)
#nullable restore
    {
        return builder
            .Matches(RegexPatterns.IntegerNumber)
            .When(x => x is not null)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.OnlyNumberValidator());
    }
}
