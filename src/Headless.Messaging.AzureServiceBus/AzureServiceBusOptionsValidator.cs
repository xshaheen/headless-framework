// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.AzureServiceBus;

internal sealed class AzureServiceBusOptionsValidator : AbstractValidator<AzureServiceBusOptions>
{
    public AzureServiceBusOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x =>
                !string.IsNullOrWhiteSpace(x.ConnectionString)
                || (!string.IsNullOrWhiteSpace(x.Namespace) && x.TokenCredential is not null)
            )
            .WithMessage(
                "Azure Service Bus requires either a ConnectionString or both Namespace and TokenCredential."
            );

        RuleFor(x => x.TopicPath).NotEmpty();
        RuleFor(x => x.MaxConcurrentCalls).GreaterThanOrEqualTo(1);
        RuleFor(x => x.SubscriptionMaxDeliveryCount).GreaterThanOrEqualTo(1);
    }
}
