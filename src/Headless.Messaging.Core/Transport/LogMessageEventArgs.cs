// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Contains event arguments for message broker log events.
/// These events are used to notify subscribers about broker health, connectivity, and operational status.
/// </summary>
public class LogMessageEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the reason or detailed description of the log event.
    /// This typically contains error messages, consumer IDs, or other contextual information.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the type of log event that occurred (e.g., ConsumerCancelled, ServerConnError, etc.).
    /// </summary>
    public MqLogType LogType { get; set; }
}
