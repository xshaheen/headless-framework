// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Diagnostics;

public class MessageEventDataPubStore
{
    public long? OperationTimestamp { get; set; }

    public string Operation { get; set; } = default!;

    public Message Message { get; set; } = default!;

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}

public class MessageEventDataPubSend
{
    public long? OperationTimestamp { get; set; }

    public string Operation { get; set; } = default!;

    public TransportMessage TransportMessage { get; set; } = default!;

    public BrokerAddress BrokerAddress { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}
