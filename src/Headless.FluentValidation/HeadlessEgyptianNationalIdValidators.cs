// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.FluentValidation.Resources;
using Headless.Validators;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>FluentValidation extension rules for Egyptian national ID numbers.</summary>
[PublicAPI]
public static class HeadlessEgyptianNationalIdValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    /// <summary>
    /// Validates an Egyptian national ID (14-digit number with a valid birth date and governorate code).
    /// Passes <see langword="null"/> values through without failure.
    /// </summary>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, string> EgyptianNationalId<T>(this IRuleBuilder<T, string> builder)
#nullable restore
    {
        // EgyptianNationalIdValidator.IsValid already enforces the 14-char length (plus digit,
        // date, and governorate checks), so a separate .Length(14) would double-report on bad input.
        return builder
            .Must(value => value is null || EgyptianNationalIdValidator.IsValid(value))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.NationalIds.InvalidEgyptianNationalId());
    }
}
