// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.Kafka;

internal sealed class MessagingKafkaOptionsValidator : AbstractValidator<MessagingKafkaOptions>
{
    public MessagingKafkaOptionsValidator()
    {
        RuleFor(x => x.Servers).NotEmpty();
        RuleFor(x => x.ConnectionPoolSize).GreaterThan(0);
    }
}
