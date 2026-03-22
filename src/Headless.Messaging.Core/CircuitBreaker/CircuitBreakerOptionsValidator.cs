// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.CircuitBreaker;

internal sealed class CircuitBreakerOptionsValidator : AbstractValidator<CircuitBreakerOptions>
{
    public CircuitBreakerOptionsValidator()
    {
        RuleFor(x => x.FailureThreshold).GreaterThan(0);
        RuleFor(x => x.OpenDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaxOpenDuration).GreaterThanOrEqualTo(x => x.OpenDuration);
        RuleFor(x => x.SuccessfulCyclesToResetEscalation).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.IsTransientException).NotNull();
    }
}
