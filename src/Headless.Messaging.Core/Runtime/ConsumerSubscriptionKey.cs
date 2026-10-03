// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Runtime;

/// <summary>
/// One subscription the host opens: a competing subscription and an every-instance subscription never share clients, even under one
/// name, because one is broker-durable and shared across processes and the other belongs to this process alone.
/// </summary>
internal readonly record struct ConsumerSubscriptionKey(
    string SubscriptionName,
    MessageLane Lane,
    Transport.ConsumerSubscriptionKind Kind = Transport.ConsumerSubscriptionKind.Competing
);
