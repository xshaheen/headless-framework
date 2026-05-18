// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Diagnostics;

[PublicAPI]
public class MessageEventDataSubStore
{
    public long? OperationTimestamp { get; set; }

    public required string Operation { get; set; }

    public TransportMessage TransportMessage { get; set; }

    public BrokerAddress BrokerAddress { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}

[PublicAPI]
public class MessageEventDataSubExecute
{
    public long? OperationTimestamp { get; set; }

    public required string Operation { get; set; }

    public required Message Message { get; set; }

    public MethodInfo? MethodInfo { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }

    /// <summary>
    /// Number of persisted retry pickups for this message. Zero on first delivery and inline retries.
    /// </summary>
    public int RetryCount { get; set; }
}
