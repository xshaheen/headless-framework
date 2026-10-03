// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging.Configuration;

internal interface IMessageCapabilityGate : IMessagingCapabilityModel
{
    void ValidateStartup(
        IEnumerable<MessageRouteKey> routes,
        bool hasDurableConsumers,
        MessagingInboxCapabilityTier requiredInboxCapability
    );

    void EnsureDirectSupported(MessageLane lane);

    void EnsureRoutingAffinitySupported(
        string messageName,
        MessageLane lane,
        string key,
        IDictionary<string, string?> headers
    );

    void EnsureOutboxSupported(MessageLane lane, bool scheduled);

    void EnsureEveryInstanceSupported(string consumerIdentity);

    void EnsureRequestReplySupported(string? responderIdentity = null);
}
