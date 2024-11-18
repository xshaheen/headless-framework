// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.BuildingBlocks.Validators;
using Framework.FluentValidation.Resources;

namespace Framework.FluentValidation;

[PublicAPI]
public static class EgyptianNationalIdValidators
{
    public static IRuleBuilderOptions<T, string?> EgyptianNationalId<T>(this IRuleBuilder<T, string?> builder)
    {
        return builder
            .Length(14)
            .Must(value => value is null || EgyptianNationalIdValidator.IsValid(value))
            .WithErrorDescriptor(FluentValidatorErrorDescriber.NationalIds.InvalidEgyptianNationalId());
    }
}
