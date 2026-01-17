// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;

namespace Framework.Messages.Diagnostics;

public class CapEventDataSubStore
{
    public long? OperationTimestamp { get; set; }

    public string Operation { get; set; } = default!;

    public TransportMessage TransportMessage { get; set; } = default!;

    public BrokerAddress BrokerAddress { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}

public class CapEventDataSubExecute
{
    public long? OperationTimestamp { get; set; }

    public string Operation { get; set; } = default!;

    public Message Message { get; set; } = default!;

    public MethodInfo? MethodInfo { get; set; }

    public long? ElapsedTimeMs { get; set; }

    public Exception? Exception { get; set; }
}
