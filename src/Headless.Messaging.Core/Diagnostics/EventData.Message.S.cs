// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Diagnostics;

public class MessageEventDataSubStore
{
    public long? OperationTimestamp { get; set; }

    public required string Operation { get; set; }

    public TransportMessage TransportMessage { get; set; }

    public BrokerAddress BrokerAddress { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}

public class MessageEventDataSubExecute
{
    public long? OperationTimestamp { get; set; }

    public required string Operation { get; set; }

    public required Message Message { get; set; }

    public MethodInfo? MethodInfo { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}
