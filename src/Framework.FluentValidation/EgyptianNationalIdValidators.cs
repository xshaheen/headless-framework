using FluentValidation;
using Framework.BuildingBlocks.Validators;
using Framework.FluentValidation.Resources;

namespace Framework.FluentValidation;

public static class EgyptianNationalIdValidators
{
    public static IRuleBuilderOptions<T, string?> EgyptianNationalId<T>(this IRuleBuilder<T, string?> builder)
    {
        return builder
            .Length(11)
            .Must(EgyptianNationalIdValidator.IsValid)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.NationalIds.InvalidEgyptianNationalId());
    }
}
