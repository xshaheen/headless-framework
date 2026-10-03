// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

[PublicAPI]
public enum InboxOperationOutcome
{
    Applied = 0,
    NotFound = 1,
    StateConflict = 2,
    Active = 3,
    Held = 4,
    OperationConflict = 5,
}
