// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Provides runtime observability into the adaptive retry polling state.
/// Resolve from DI to monitor backpressure behavior.
/// </summary>
public interface IRetryProcessorMonitor
{
    /// <summary>Current effective polling interval (adapts based on circuit-open rate).</summary>
    TimeSpan CurrentPollingInterval { get; }

    /// <summary>Whether the retry processor has backed off from its base interval.</summary>
    bool IsBackedOff { get; }

    /// <summary>
    /// Resets the adaptive polling interval to the base value and clears all cycle counters.
    /// This is the operator/agent manual recovery path for retry backpressure.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    ValueTask ResetBackpressureAsync(CancellationToken ct = default);
}
