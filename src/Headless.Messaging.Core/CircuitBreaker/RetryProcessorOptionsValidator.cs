// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.CircuitBreaker;

internal sealed class RetryProcessorOptionsValidator : AbstractValidator<RetryProcessorOptions>
{
    public RetryProcessorOptionsValidator()
    {
        RuleFor(x => x.MaxPollingInterval).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.CircuitOpenRateThreshold).ExclusiveBetween(0, 1);
    }
}
