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

internal enum RuntimeConsumerRegistrationStatus
{
    Attached = 0,
    Ignored = 1,
}

internal sealed record RuntimeConsumerRegistrationResult(
    RuntimeConsumerRegistrationStatus Status,
    string? SubscriptionId,
    string MessageName,
    string Identity,
    string HandlerId,
    MessageLane Lane
);

internal sealed record RuntimeConsumerRegistration(
    string SubscriptionId,
    string MessageName,
    string Identity,
    string HandlerId,
    MessageLane Lane,
    ConsumerExecutorDescriptor Descriptor,
    IRuntimeMessageHandlerInvoker Invoker
);
