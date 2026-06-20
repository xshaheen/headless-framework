// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using Headless.Validators;

namespace FluentValidation;

[PublicAPI]
public static class EgyptianNationalIdValidators
{
    public static IRuleBuilderOptions<T, string?> EgyptianNationalId<T>(this IRuleBuilder<T, string?> builder)
    {
        // EgyptianNationalIdValidator.IsValid already enforces the 14-char length (plus digit,
        // date, and governorate checks), so a separate .Length(14) would double-report on bad input.
        return builder
            .Must(value => value is null || EgyptianNationalIdValidator.IsValid(value))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.NationalIds.InvalidEgyptianNationalId());
    }
}
