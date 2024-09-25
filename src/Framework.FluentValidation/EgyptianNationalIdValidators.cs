// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using Framework.FluentValidation.Resources;
using Framework.Kernel.BuildingBlocks.Validators;

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
