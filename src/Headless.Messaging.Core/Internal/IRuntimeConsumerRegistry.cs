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

internal interface IRuntimeConsumerRegistry
{
    IReadOnlyList<ConsumerExecutorDescriptor> GetDescriptors();

    RuntimeConsumerRegistrationResult Register<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null
    )
        where TMessage : class;

    bool Unregister(string subscriptionId);

    bool TryGetInvoker(
        string messageName,
        string identity,
        string handlerId,
        MessageLane lane,
        [NotNullWhen(true)] out IRuntimeMessageHandlerInvoker? invoker
    );
}
