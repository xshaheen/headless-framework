// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Couchbase.Managers;

/// <summary>
/// Resilience options for <see cref="ICouchbaseManager"/> operations. Controls the Polly retry
/// pipeline (linear back-off with jitter plus an overall timeout) that guards every mutating
/// manager operation.
/// </summary>
[PublicAPI]
public sealed class CouchbaseManagerOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retries, in addition to the original attempt.
    /// Defaults to 3. Must be positive.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retries. The back-off is linear (each delay grows by this
    /// value) with jitter applied. Defaults to 500 milliseconds. Must not be negative.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the overall timeout applied around each manager operation, including all its
    /// retries. Defaults to 10 seconds. Must be greater than 10 milliseconds and less than 24 hours
    /// (Polly's timeout bounds).
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

internal sealed class CouchbaseManagerOptionsValidator : AbstractValidator<CouchbaseManagerOptions>
{
    public CouchbaseManagerOptionsValidator()
    {
        RuleFor(x => x.MaxRetries).GreaterThan(0);
        RuleFor(x => x.RetryDelay).GreaterThanOrEqualTo(TimeSpan.Zero);

        // Mirror Polly's TimeoutStrategyOptions bounds so misconfiguration fails at options
        // validation (startup) instead of at pipeline build.
        RuleFor(x => x.Timeout).GreaterThan(TimeSpan.FromMilliseconds(10)).LessThan(TimeSpan.FromHours(24));
    }
}
