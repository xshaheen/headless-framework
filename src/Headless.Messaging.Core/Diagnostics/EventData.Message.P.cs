// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Diagnostics;

[PublicAPI]
public class MessageEventDataPubStore
{
    public long? OperationTimestamp { get; set; }

    public string Operation { get; set; } = null!;

    public Message Message { get; set; } = null!;

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
public class MessageEventDataPubSend
{
    public long? OperationTimestamp { get; set; }

    public string Operation { get; set; } = null!;

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
