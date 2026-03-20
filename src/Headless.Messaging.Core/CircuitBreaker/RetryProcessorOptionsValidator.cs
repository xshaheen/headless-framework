// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

internal sealed class RetryProcessorOptionsValidator : IValidateOptions<RetryProcessorOptions>
{
    public ValidateOptionsResult Validate(string? name, RetryProcessorOptions options)
    {
        var failures = new List<string>();

        if (options.MaxPollingInterval <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(RetryProcessorOptions.MaxPollingInterval)} must be greater than TimeSpan.Zero.");
        }

        if (options.CircuitOpenRateThreshold is <= 0 or >= 1)
        {
            failures.Add(
                $"{nameof(RetryProcessorOptions.CircuitOpenRateThreshold)} must be strictly between 0 and 1 "
                    + $"(got {options.CircuitOpenRateThreshold})."
            );
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
