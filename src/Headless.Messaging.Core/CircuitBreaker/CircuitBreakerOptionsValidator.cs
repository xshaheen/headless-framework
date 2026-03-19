// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

internal sealed class CircuitBreakerOptionsValidator : IValidateOptions<CircuitBreakerOptions>
{
    public ValidateOptionsResult Validate(string? name, CircuitBreakerOptions options)
    {
        var failures = new List<string>();

        if (options.FailureThreshold <= 0)
        {
            failures.Add($"{nameof(CircuitBreakerOptions.FailureThreshold)} must be greater than 0.");
        }

        if (options.MaxOpenDuration < options.OpenDuration)
        {
            failures.Add(
                $"{nameof(CircuitBreakerOptions.MaxOpenDuration)} ({options.MaxOpenDuration}) "
                    + $"must be greater than or equal to {nameof(CircuitBreakerOptions.OpenDuration)} ({options.OpenDuration})."
            );
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (options.IsTransientException is null)
        {
            failures.Add($"{nameof(CircuitBreakerOptions.IsTransientException)} must not be null.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
