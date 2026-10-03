// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging;

internal enum InboxMetricOutcome
{
    Winner = 0,
    InFlightDuplicate = 1,
    SucceededDuplicate = 2,
    TerminalFailedDuplicate = 3,
    Reserved = 4,
    Succeeded = 5,
    FailedExhausted = 6,
    Orphaned = 7,
    Routable = 8,
    Held = 9,
    Released = 10,
    Purged = 11,
    Replayed = 12,
    Expired = 13,
}
