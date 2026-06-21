// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Diagnostics;

[PublicAPI]
public class MessageEventDataSubStore
{
    public long? OperationTimestamp { get; set; }

    public required string Operation { get; set; }

    public TransportMessage TransportMessage { get; set; }

    public BrokerAddress BrokerAddress { get; set; }

    public IntentType IntentType { get; set; } = IntentType.Bus;

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }

    /// <summary>
    /// Cancellation token flowing from the originating messaging operation. Forwarded to
    /// any <c>IActivityTagEnricher</c> implementations so they can cooperate with shutdown
    /// or request cancellation. Defaults to <see cref="CancellationToken.None"/>.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

[PublicAPI]
public class MessageEventDataSubExecute
{
    public long? OperationTimestamp { get; set; }

    public required string Operation { get; set; }

    public required Message Message { get; set; }

    public IntentType IntentType { get; set; } = IntentType.Bus;

    public MethodInfo? MethodInfo { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }

    /// <summary>
    /// Number of persisted retry pickups for this message. Zero on first delivery and inline retries.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Cancellation token flowing from the originating messaging operation. Forwarded to
    /// any <c>IActivityTagEnricher</c> implementations so they can cooperate with shutdown
    /// or request cancellation. Defaults to <see cref="CancellationToken.None"/>.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
