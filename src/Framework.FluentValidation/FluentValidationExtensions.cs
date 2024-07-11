using Framework.BuildingBlocks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace FluentValidation;

public static class FluentValidationExtensions
{
    public static IRuleBuilderOptions<T, TProperty> WithErrorDescriptor<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        ErrorDescriptor errorDescriptor
    )
    {
        return rule.WithErrorCode(errorDescriptor.Code)
            .WithMessage(
                string.IsNullOrWhiteSpace(errorDescriptor.Description)
                    ? errorDescriptor.Code
                    : errorDescriptor.Description
            );
    }

    public static Severity ToSeverity(this ValidationSeverity severity)
    {
        return severity switch
        {
            ValidationSeverity.Information => Severity.Info,
            ValidationSeverity.Warning => Severity.Warning,
            ValidationSeverity.Error => Severity.Error,
            > ValidationSeverity.Error => Severity.Error,
            < ValidationSeverity.Information => Severity.Info,
        };
    }
}
