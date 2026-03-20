// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Configuration options for the retry processor's adaptive polling and backpressure behavior.
/// </summary>
public sealed class RetryProcessorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the retry processor uses adaptive polling intervals.
    /// When <see langword="true"/>, the polling interval backs off under sustained transient failure
    /// conditions and recovers as the failure rate drops.
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
    /// Gets or sets the transient failure rate above which the retry processor will back off.
    /// Expressed as a fraction between 0 (exclusive) and 1 (exclusive). When the observed
    /// transient failure rate exceeds this threshold, the processor slows its polling to reduce
    /// amplification of an unhealthy dependency.
    /// Default is 0.8 (80%).
    /// </summary>
    public double TransientFailureRateThreshold { get; set; } = 0.8;
}
