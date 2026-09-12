// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Pulsar;

internal static class PulsarRoutingAffinity
{
    internal static readonly MessagingRoutingAffinityMapping Mapping = new(PulsarMessagingHeaders.PulsarKey);
}
