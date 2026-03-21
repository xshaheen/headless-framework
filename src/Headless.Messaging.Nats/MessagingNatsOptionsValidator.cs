// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.Nats;

internal sealed class MessagingNatsOptionsValidator : AbstractValidator<MessagingNatsOptions>
{
    public MessagingNatsOptionsValidator()
    {
        RuleFor(x => x.Servers).NotEmpty();
        RuleFor(x => x.ConnectionPoolSize).GreaterThan(0);
    }
}
