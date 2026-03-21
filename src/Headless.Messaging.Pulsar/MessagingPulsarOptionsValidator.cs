// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.Pulsar;

internal sealed class MessagingPulsarOptionsValidator : AbstractValidator<MessagingPulsarOptions>
{
    public MessagingPulsarOptionsValidator()
    {
        RuleFor(x => x.ServiceUrl).NotEmpty();
    }
}
