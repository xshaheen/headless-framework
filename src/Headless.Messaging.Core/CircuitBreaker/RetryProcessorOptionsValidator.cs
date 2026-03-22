// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

internal sealed class RetryProcessorOptionsValidator : AbstractValidator<RetryProcessorOptions>
{
    private readonly TimeSpan _failedRetryInterval;

    public RetryProcessorOptionsValidator(IOptions<MessagingOptions> messagingOptions)
    {
        _failedRetryInterval = TimeSpan.FromSeconds(messagingOptions.Value.FailedRetryInterval);

        RuleFor(x => x.MaxPollingInterval)
            .GreaterThan(TimeSpan.Zero)
            .LessThanOrEqualTo(TimeSpan.FromHours(24))
            .GreaterThanOrEqualTo(_failedRetryInterval)
            .WithMessage(
                $"MaxPollingInterval must be greater than or equal to the failed retry interval ({_failedRetryInterval})."
            );
        RuleFor(x => x.CircuitOpenRateThreshold).ExclusiveBetween(0, 1);
    }
}
