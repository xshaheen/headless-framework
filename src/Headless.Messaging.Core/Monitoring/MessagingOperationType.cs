// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

[PublicAPI]
public enum MessagingOperationType
{
    Hold = 0,
    ReleaseHold = 1,
    ForceReprocess = 2,
    Purge = 3,
    Cleanup = 4,
    Revoke = 5,
    DispatchNow = 6,
}
