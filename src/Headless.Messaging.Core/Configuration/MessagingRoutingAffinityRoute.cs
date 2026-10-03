// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>A registered logical destination and its immutable native affinity mapping.</summary>
[PublicAPI]
public sealed record MessagingRoutingAffinityRoute(
    MessageLane Lane,
    string MessageName,
    MessagingRoutingAffinityMapping Mapping
);
