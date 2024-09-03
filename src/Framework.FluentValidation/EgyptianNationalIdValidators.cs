using FluentValidation;
using Framework.FluentValidation.Resources;
using Framework.Kernel.BuildingBlocks.Validators;

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
