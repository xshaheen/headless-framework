// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api;

internal sealed class MultiTenancyOptionsValidator : AbstractValidator<MultiTenancyOptions>
{
    public MultiTenancyOptionsValidator()
    {
        RuleFor(x => x.ClaimType).NotEmpty();
    }
}
