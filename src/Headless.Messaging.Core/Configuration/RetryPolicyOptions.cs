// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Messages;
using Headless.Messaging.Retry;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Configures message retry behavior across inline and persisted retry paths.
/// </summary>
[PublicAPI]
public sealed class RetryPolicyOptions
{
    /// <summary>
    /// Gets or sets the total number of delivery attempts, including the first non-retry execution.
    /// Set to 1 to disable retry entirely. Default is 50.
    /// </summary>
    public int MaxAttempts { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum number of retries to run inline before scheduling a persisted retry.
    /// Default is 2.
    /// </summary>
    public int MaxInlineRetries { get; set; } = 2;

    /// <summary>
    /// Gets or sets the backoff strategy used to compute per-attempt delay.
    /// Defaults to exponential backoff.
    /// </summary>
    public IRetryBackoffStrategy BackoffStrategy { get; set; } = new ExponentialBackoffStrategy();

    /// <summary>
    /// Gets or sets the callback invoked once retry attempts are exhausted
    /// (<see cref="MaxAttempts"/> reached or the configured <see cref="BackoffStrategy"/> signals
    /// no further delay).
    /// Permanent failures (for example argument validation errors or subscriber-not-found) and
    /// cancellations short-circuit the retry budget entirely and do not invoke this callback.
    /// The callback runs synchronously inside the live dispatch scope carried by
    /// <see cref="FailedInfo.ServiceProvider"/>.
    /// </summary>
    /// <remarks>
    /// The callback is synchronous (<see cref="Action{T}"/>). Do NOT capture
    /// <see cref="FailedInfo.ServiceProvider"/> inside a fire-and-forget continuation
    /// (for example <c>Task.Run(...)</c>): the dispatch scope is disposed as soon as
    /// the callback returns, and a deferred resolution will throw
    /// <see cref="ObjectDisposedException"/>. If you need async work, resolve services
    /// synchronously and capture concrete values, or wait for the async-callback shape
    /// in a future release.
    /// </remarks>
    public Action<FailedInfo>? OnExhausted { get; set; }
}

internal sealed class RetryPolicyOptionsValidator : AbstractValidator<RetryPolicyOptions>
{
    public RetryPolicyOptionsValidator()
    {
        RuleFor(x => x.MaxAttempts).GreaterThanOrEqualTo(1);
        RuleFor(x => x.MaxInlineRetries).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxInlineRetries)
            .LessThan(x => x.MaxAttempts)
            .WithMessage("MaxInlineRetries must be less than MaxAttempts.");
        RuleFor(x => x.BackoffStrategy).NotNull();
    }
}
