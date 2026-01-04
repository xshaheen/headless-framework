// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Resources;
using Framework.Constants;

namespace FluentValidation;

[PublicAPI]
public static class StringsFluentValidationExtensions
{
#nullable disable
    public static IRuleBuilderOptions<T, string> OnlyIntegers<T>(this IRuleBuilder<T, string> builder)
#nullable restore
    {
        return builder
            .Matches(RegexPatterns.IntegerNumber)
            .When(x => x is not null)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.OnlyNumberValidator());
    }
}
