// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

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
}
