// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Reliability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal sealed class EmptyRuntimeConsumerRegistry : IRuntimeConsumerRegistry
{
    public static EmptyRuntimeConsumerRegistry Instance { get; } = new();

    public IReadOnlyList<ConsumerExecutorDescriptor> GetDescriptors()
    {
        return [];
    }

    public RuntimeConsumerRegistrationResult Register<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null
    )
        where TMessage : class
    {
        throw new InvalidOperationException("Runtime consumer registry is not available.");
    }

    public bool Unregister(string subscriptionId)
    {
        return false;
    }

    public bool TryGetInvoker(
        string messageName,
        string identity,
        string handlerId,
        MessageLane lane,
        [NotNullWhen(true)] out IRuntimeMessageHandlerInvoker? invoker
    )
    {
        invoker = null;
        return false;
    }
}
