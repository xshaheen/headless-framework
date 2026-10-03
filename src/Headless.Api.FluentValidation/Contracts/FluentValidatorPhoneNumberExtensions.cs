// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.Contracts;

[PublicAPI]
public static class FluentValidatorPhoneNumberExtensions
{
    /// <summary>
    /// Applies <c>PhoneNumberRequestValidator</c> to validate the country code and subscriber
    /// number formats. The validator is skipped when the <paramref name="builder"/> value is
    /// <see langword="null"/> (the property is treated as not provided).
    /// </summary>
    /// <returns>The rule builder so that additional calls can be chained.</returns>
    public static IRuleBuilderOptions<T, PhoneNumberRequest?> PhoneNumber<T>(
        this IRuleBuilder<T, PhoneNumberRequest?> builder
    )
    {
        return builder.SetValidator(new PhoneNumberRequestValidator()!).When(x => x is not null);
    }
}
