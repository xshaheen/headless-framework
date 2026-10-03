// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>Target kind for generalized messaging operations.</summary>
[PublicAPI]
public enum MessagingOperationTargetKind
{
    Inbox = 0,
    ScheduledDelivery = 1,
}
