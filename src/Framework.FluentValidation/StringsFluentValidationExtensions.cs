// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Framework.FluentValidation.Resources;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace FluentValidation;

[PublicAPI]
public static class StringsFluentValidationExtensions
{
    public static IRuleBuilderOptions<T, string> OnlyIntegers<T>(this IRuleBuilder<T, string> builder)
    {
        return builder
            .Matches(RegexPatterns.IntegerNumber)
            .When(x => x is not null)
            .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.OnlyNumberValidator());
    }
}
