// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.Contracts;

internal sealed class PhoneNumberRequestValidator : AbstractValidator<PhoneNumberRequest>
{
    public PhoneNumberRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().PhoneCountryCode();
        RuleFor(x => x.Number).NotEmpty().PhoneNumber(x => x.Code);
    }
}
