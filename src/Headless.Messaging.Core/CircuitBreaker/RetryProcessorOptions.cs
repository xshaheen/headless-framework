// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Configuration options for the retry processor's adaptive polling and backpressure behavior.
/// </summary>
public sealed class RetryProcessorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the retry processor uses adaptive polling intervals.
    /// When <see langword="true"/>, the polling interval backs off when a high fraction of messages
    /// are skipped due to open circuits, and recovers as the skip rate drops.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool AdaptivePolling { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum polling interval when adaptive polling is enabled.
    /// The processor will not wait longer than this value between retry cycles, regardless of
    /// the observed failure rate. Must be greater than <see cref="TimeSpan.Zero"/>.
    /// Default is 15 minutes.
    /// </summary>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the circuit-open rate above which the retry processor will back off.
    /// Expressed as a fraction between 0 (exclusive) and 1 (exclusive). When the proportion
    /// of messages skipped due to open circuits exceeds this threshold, the processor slows
    /// its polling to reduce amplification of an unhealthy dependency.
    /// Default is 0.8 (80%).
    /// </summary>
    public double CircuitOpenRateThreshold { get; set; } = 0.8;
}

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
