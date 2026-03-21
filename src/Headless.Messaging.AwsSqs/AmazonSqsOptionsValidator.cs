// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.AwsSqs;

internal sealed class AmazonSqsOptionsValidator : AbstractValidator<AmazonSqsOptions>
{
    public AmazonSqsOptionsValidator()
    {
        RuleFor(x => x.Region).NotNull();
    }
}
